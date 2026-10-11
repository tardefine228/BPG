using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Marks a destructible object that must be knocked down to clear a level.
/// One component covers both block types used by the game:
/// <list type="bullet">
/// <item>ordinary blocks, knocked down by the first strong hit
/// (<c>hitsToClear</c> = 1, this is how level 1 is built);</item>
/// <item>reinforced blocks, which survive several hits, keep moving on every
/// impact and are only knocked down after the configured number of hits.</item>
/// </list>
/// The durability is a plain Inspector field, so every block can be tuned
/// without touching the code.
/// </summary>
public sealed class LevelTarget : MonoBehaviour
{
    private const float IgnoreCollisionsFor = 0.8f;
    private const int HitCounterSortingOrder = 82;

    [Header("Сбивание")]
    [Tooltip("Блок считается сбитым, если упал ниже этой отметки.")]
    [SerializeField] private float fallY = -6f;

    [Tooltip("Минимальная скорость столкновения, при которой удар вообще засчитывается.")]
    [SerializeField, Min(0f)] private float impactSpeed = 2f;

    [Header("Прочность блока")]
    [Tooltip("Сколько ударов выдерживает блок. 1 — обычный блок (как в 1 уровне). " +
             "3–5 — бронированный: он двигается от каждого удара, но сносится только " +
             "после нужного количества попаданий.")]
    [SerializeField, Min(1)] private int hitsToClear = 1;

    [Tooltip("Пауза между засчитанными ударами, чтобы одно касание снаряда не сняло сразу несколько единиц прочности.")]
    [SerializeField, Min(0f)] private float hitCooldown = 0.35f;

    [Header("Отклик на удары")]
    [Tooltip("Подсвечивать попадания у бронированных блоков (вспышка + затемнение по мере износа).")]
    [SerializeField] private bool showHitFeedback = true;

    [Tooltip("Цвет блока, когда прочность кончилась.")]
    [SerializeField] private Color damagedTint = new Color(0.32f, 0.31f, 0.36f, 1f);

    [Tooltip("Цвет вспышки в момент удара.")]
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.85f, 0.4f, 1f);

    [Tooltip("Длительность вспышки, секунды.")]
    [SerializeField, Min(0f)] private float hitFlashDuration = 0.12f;

    [Header("Развал после последнего удара")]
    [Tooltip("Отбросить блок в сторону выстрела, когда он получил последний удар.")]
    [SerializeField] private bool breakOnCleared;

    [Tooltip("Сила отбрасывания блока после последнего удара.")]
    [SerializeField, Min(0f)] private float breakImpulse = 6f;

    [Tooltip("Вращение блока после последнего удара.")]
    [SerializeField, Min(0f)] private float breakTorque = 30f;

    [Header("Счётчик ударов")]
    [Tooltip("Показывать над блоком, сколько ударов ещё нужно.")]
    [SerializeField] private bool showRemainingHits;

    [Tooltip("Смещение счётчика относительно центра блока (в юнитах сцены).")]
    [SerializeField] private Vector3 hitCounterOffset = new Vector3(0f, 2.4f, 0f);

    [Tooltip("Цвет цифры счётчика ударов.")]
    [SerializeField] private Color hitCounterColor = new Color32(230, 155, 82, 255);

    [Header("События")]
    [Tooltip("Вызывается после каждого удара, который не снёс блок.")]
    [SerializeField] private UnityEvent onHit = new UnityEvent();

    [Tooltip("Вызывается, когда блок сбит (любым способом).")]
    [SerializeField] private UnityEvent onCleared = new UnityEvent();

    private readonly List<SpriteRenderer> damageSprites = new List<SpriteRenderer>();
    private Color[] baseColors = new Color[0];

    private Rigidbody2D body;
    private Vector3 startPosition;
    private float nextHitTime;
    private float counterPopUntil;
    private Coroutine flashRoutine;
    private Canvas counterCanvas;
    private RectTransform counterRect;
    private TMP_Text counterText;
    private Camera counterCamera;

    public bool IsCleared { get; private set; }
    public float MaxTravelDistance { get; private set; }

    /// <summary>How many hits the block already took.</summary>
    public int HitsTaken { get; private set; }

    /// <summary>Total number of hits the block can take (1 for ordinary blocks).</summary>
    public int HitsToClear => Mathf.Max(1, hitsToClear);

    /// <summary>Hits left before the block is knocked down.</summary>
    public int HitsRemaining => Mathf.Max(0, HitsToClear - HitsTaken);

    /// <summary>True for multi-hit (reinforced) blocks.</summary>
    public bool IsReinforced => HitsToClear > 1;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        damageSprites.AddRange(GetComponentsInChildren<SpriteRenderer>(true));
        baseColors = new Color[damageSprites.Count];
        for (int i = 0; i < damageSprites.Count; i++)
        {
            baseColors[i] = damageSprites[i].color;
        }

        startPosition = transform.position;
        ApplyDamageTint();
        BuildHitCounter();
    }

    private void Update()
    {
        float distance = Vector2.Distance(transform.position, startPosition);
        if (distance > MaxTravelDistance)
        {
            MaxTravelDistance = distance;
        }

        UpdateHitCounter();

        if (IsCleared)
        {
            return;
        }

        if (transform.position.y <= fallY)
        {
            ClearTarget(Vector2.zero, false);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsCleared || collision == null || collision.collider == null)
        {
            return;
        }

        if (Time.timeSinceLevelLoad < IgnoreCollisionsFor)
        {
            return;
        }

        if (collision.collider.GetComponent<LevelTarget>() != null)
        {
            return;
        }

        if (collision.rigidbody != null && collision.rigidbody.bodyType == RigidbodyType2D.Static)
        {
            return;
        }

        Vector2 relativeVelocity = collision.relativeVelocity;
        if (relativeVelocity.sqrMagnitude < impactSpeed * impactSpeed)
        {
            return;
        }

        // Reinforced blocks ignore follow-up contacts that arrive too soon, so a
        // single shell cannot drain several durability points in one bounce.
        if (IsReinforced && Time.time < nextHitTime)
        {
            return;
        }

        nextHitTime = Time.time + hitCooldown;
        HitsTaken++;

        if (HitsTaken >= HitsToClear)
        {
            ClearTarget(relativeVelocity.normalized, true);
        }
        else
        {
            PlayHitFeedback();
        }
    }

    private void PlayHitFeedback()
    {
        ApplyDamageTint();
        counterPopUntil = Time.time + 0.18f;

        if (!IsReinforced || !showHitFeedback || damageSprites.Count == 0)
        {
            onHit.Invoke();
            return;
        }

        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
        }

        flashRoutine = StartCoroutine(FlashRoutine());
        onHit.Invoke();
    }

    private IEnumerator FlashRoutine()
    {
        SetSpriteColors(hitFlashColor);
        yield return new WaitForSeconds(Mathf.Max(0.02f, hitFlashDuration));
        ApplyDamageTint();
        flashRoutine = null;
    }

    private void ClearTarget(Vector2 hitDirection, bool byImpact)
    {
        if (IsCleared)
        {
            return;
        }

        IsCleared = true;
        ApplyDamageTint();
        HideHitCounter();

        if (byImpact && breakOnCleared && hitDirection.sqrMagnitude > 0.0001f)
        {
            BreakApart(hitDirection);
        }

        onCleared.Invoke();
    }

    /// <summary>
    /// Knocks the spent block away from the shot. The collider stays enabled on
    /// purpose: the block must keep existing for the level session, which counts
    /// destroyed targets by component, not by object lifetime.
    /// </summary>
    private void BreakApart(Vector2 hitDirection)
    {
        if (body == null || body.bodyType != RigidbodyType2D.Dynamic)
        {
            return;
        }

        body.AddForce(hitDirection * breakImpulse, ForceMode2D.Impulse);
        float spin = hitDirection.x >= 0f ? -1f : 1f;
        body.AddTorque(spin * breakTorque, ForceMode2D.Impulse);
    }

    private void ApplyDamageTint()
    {
        if (!showHitFeedback || !IsReinforced)
        {
            SetSpriteColors(null);
            return;
        }

        float damage = IsCleared ? 1f : (float)HitsTaken / HitsToClear;
        for (int i = 0; i < damageSprites.Count; i++)
        {
            SpriteRenderer sprite = damageSprites[i];
            if (sprite == null)
            {
                continue;
            }

            sprite.color = Color.Lerp(baseColors[i], damagedTint, damage);
        }
    }

    private void SetSpriteColors(Color? color)
    {
        for (int i = 0; i < damageSprites.Count; i++)
        {
            SpriteRenderer sprite = damageSprites[i];
            if (sprite == null)
            {
                continue;
            }

            sprite.color = color ?? baseColors[i];
        }
    }

    private void BuildHitCounter()
    {
        if (!showRemainingHits || !IsReinforced)
        {
            return;
        }

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0)
        {
            uiLayer = 5;
        }

        GameObject canvasObject = new GameObject("HitCounterCanvas", typeof(RectTransform), typeof(Canvas));
        canvasObject.layer = uiLayer;
        counterCanvas = canvasObject.GetComponent<Canvas>();
        counterCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        counterCanvas.sortingOrder = HitCounterSortingOrder;

        GameObject label = new GameObject("HitsLeft", typeof(RectTransform));
        label.layer = uiLayer;
        counterRect = label.GetComponent<RectTransform>();
        counterRect.SetParent(canvasObject.transform, false);
        counterRect.anchorMin = Vector2.zero;
        counterRect.anchorMax = Vector2.zero;
        counterRect.pivot = new Vector2(0.5f, 0.5f);
        counterRect.sizeDelta = new Vector2(160f, 90f);

        counterText = label.AddComponent<TextMeshProUGUI>();
        counterText.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        counterText.text = HitsRemaining.ToString();
        counterText.fontSize = 54f;
        counterText.fontStyle = FontStyles.Bold;
        counterText.alignment = TextAlignmentOptions.Center;
        counterText.color = hitCounterColor;
        counterText.raycastTarget = false;
    }

    private void UpdateHitCounter()
    {
        if (counterText == null || counterRect == null)
        {
            return;
        }

        if (IsCleared)
        {
            HideHitCounter();
            return;
        }

        counterText.text = HitsRemaining.ToString();

        if (counterCamera == null)
        {
            counterCamera = Camera.main;
            if (counterCamera == null)
            {
                return;
            }
        }

        Vector3 screenPoint = counterCamera.WorldToScreenPoint(transform.position + hitCounterOffset);
        if (screenPoint.z <= 0f)
        {
            counterCanvas.enabled = false;
            return;
        }

        counterCanvas.enabled = true;
        counterRect.anchoredPosition = new Vector2(screenPoint.x, screenPoint.y);

        // Small pop so the player notices that the durability went down.
        float popT = Mathf.Clamp01((counterPopUntil - Time.time) / 0.18f);
        float scale = 1f + 0.45f * popT;
        counterRect.localScale = new Vector3(scale, scale, 1f);
    }

    private void HideHitCounter()
    {
        if (counterText != null)
        {
            counterText.gameObject.SetActive(false);
        }

        if (counterCanvas != null)
        {
            counterCanvas.enabled = false;
        }
    }

    private void OnDestroy()
    {
        if (counterCanvas != null)
        {
            Destroy(counterCanvas.gameObject);
        }
    }
}
