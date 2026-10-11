using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// Мышь — главная цель уровня, как свинья в Angry Birds: уровень пройден, когда побеждены все мыши.
// Удары отнимают жизни, а касание земли (тег Ground или любая неподвижная поверхность) отнимает все жизни сразу.
// Спрайт временный: замените его в префабе Assets/Prefabs/Mouse.prefab и подгоните капсулу под новую текстуру.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public sealed class MouseTarget : MonoBehaviour
{
    [Header("Жизни")]
    [Tooltip("Сколько ударов выдерживает мышь. Касание земли отнимает все жизни сразу.")]
    [SerializeField, Min(1)] private int lives = 2;

    [Tooltip("Минимальная скорость столкновения, которая отнимает одну жизнь.")]
    [SerializeField, Min(0f)] private float damageSpeed = 4f;

    [Tooltip("Скорость столкновения, при которой мышь сразу теряет все жизни (например, прямое попадание кота).")]
    [SerializeField, Min(0f)] private float lethalSpeed = 10f;

    [Tooltip("Пауза между засчитанными ударами, чтобы одно касание не сняло несколько жизней.")]
    [SerializeField, Min(0f)] private float hitCooldown = 0.2f;

    [Tooltip("Сколько секунд после загрузки уровня удары и земля не засчитываются, пока постройка оседает.")]
    [SerializeField, Min(0f)] private float settleTime = 1f;

    [Header("Земля")]
    [Tooltip("Объекты с этим тегом считаются землёй (тег стоит на FlatGround каждого уровня).")]
    [SerializeField] private string groundTag = "Ground";

    [Tooltip("Любая неподвижная (Static) поверхность тоже считается землёй: каменные подиумы и площадки.")]
    [SerializeField] private bool staticSurfacesAreGround = true;

    [Tooltip("Мышь побеждена, если упала ниже этой отметки (за край карты).")]
    [SerializeField] private float fallY = -6f;

    [Header("Отклик")]
    [Tooltip("Цвет вспышки в момент удара.")]
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.55f, 0.55f, 1f);

    [Tooltip("Длительность вспышки, секунды.")]
    [SerializeField, Min(0f)] private float hitFlashDuration = 0.12f;

    [Tooltip("Цвет раненой мыши, у которой осталась часть жизней.")]
    [SerializeField] private Color woundedTint = new Color(0.86f, 0.74f, 0.78f, 1f);

    [Tooltip("За сколько секунд побеждённая мышь сжимается и исчезает.")]
    [SerializeField, Min(0f)] private float vanishDuration = 0.3f;

    [Tooltip("Материал с шейдером BPG/ComicFX для облачка пыли. Пустое поле — без облачка.")]
    [SerializeField] private Material effectMaterial;

    [Tooltip("Цвет облачка пыли, когда мышь побеждена.")]
    [SerializeField] private Color poofColor = new Color(0.86f, 0.86f, 0.9f, 1f);

    [Header("События")]
    [Tooltip("Вызывается после удара, который отнял жизнь, но не победил мышь.")]
    [SerializeField] private UnityEvent onHit = new UnityEvent();

    [Tooltip("Вызывается, когда мышь побеждена (любым способом).")]
    [SerializeField] private UnityEvent onDefeated = new UnityEvent();

    private readonly List<SpriteRenderer> sprites = new List<SpriteRenderer>();
    private Color[] baseColors = new Color[0];
    private Rigidbody2D body;
    private float nextHitTime;
    private Coroutine flashRoutine;
    private Vector3 baseScale;

    public bool IsDefeated { get; private set; }

    // Полный запас жизней и сколько жизней осталось сейчас.
    public int Lives => Mathf.Max(1, lives);
    public int LivesLeft { get; private set; }

    public Rigidbody2D Body => body;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        sprites.AddRange(GetComponentsInChildren<SpriteRenderer>(true));
        baseColors = new Color[sprites.Count];
        for (int i = 0; i < sprites.Count; i++)
            baseColors[i] = sprites[i].color;
        baseScale = transform.localScale;
        LivesLeft = Lives;
    }

    private void Update()
    {
        if (!IsDefeated && transform.position.y <= fallY)
            Defeat();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsDefeated || collision == null || collision.collider == null || IsSettling)
            return;

        if (IsGround(collision.collider))
        {
            Defeat();
            return;
        }

        float speed = collision.relativeVelocity.magnitude;
        if (speed >= lethalSpeed)
        {
            TakeDamage(LivesLeft);
            return;
        }

        if (speed < damageSpeed || Time.time < nextHitTime)
            return;
        nextHitTime = Time.time + hitCooldown;
        TakeDamage(1);
    }

    // Мышь, которая сползла на землю медленно или лежала на ней во время оседания, тоже побеждена.
    private void OnCollisionStay2D(Collision2D collision)
    {
        if (!IsDefeated && collision != null && collision.collider != null && !IsSettling
            && IsGround(collision.collider))
            Defeat();
    }

    private bool IsSettling => Time.timeSinceLevelLoad < settleTime;

    private bool IsGround(Collider2D other)
    {
        if (!string.IsNullOrEmpty(groundTag) && other.CompareTag(groundTag))
            return true;
        if (!staticSurfacesAreGround)
            return false;
        Rigidbody2D otherBody = other.attachedRigidbody;
        return otherBody == null || otherBody.bodyType == RigidbodyType2D.Static;
    }

    private void TakeDamage(int amount)
    {
        if (IsDefeated || amount <= 0)
            return;
        LivesLeft = Mathf.Max(0, LivesLeft - amount);
        if (LivesLeft == 0)
        {
            Defeat();
            return;
        }

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine());
        onHit.Invoke();
    }

    // Мгновенно побеждает мышь: касание земли, падение за карту или последний удар.
    public void Defeat()
    {
        if (IsDefeated)
            return;
        IsDefeated = true;
        LivesLeft = 0;
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }

        onDefeated.Invoke();
        if (isActiveAndEnabled)
            StartCoroutine(VanishRoutine());
        else
            gameObject.SetActive(false);
    }

    private IEnumerator FlashRoutine()
    {
        SetSpriteColors(hitFlashColor, false);
        yield return new WaitForSeconds(Mathf.Max(0.02f, hitFlashDuration));
        SetSpriteColors(woundedTint, true);
        flashRoutine = null;
    }

    private IEnumerator VanishRoutine()
    {
        // Побеждённая мышь сразу выходит из физики, чтобы не мешать обломкам.
        if (body != null)
            body.simulated = false;
        PlayPoof();
        float duration = Mathf.Max(0.01f, vanishDuration);
        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            float t = time / duration;
            // Короткое «вспухание», затем сжатие в точку.
            float scale = t < 0.3f ? Mathf.Lerp(1f, 1.2f, t / 0.3f) : Mathf.Lerp(1.2f, 0f, (t - 0.3f) / 0.7f);
            transform.localScale = baseScale * scale;
            yield return null;
        }

        gameObject.SetActive(false);
    }

    private void PlayPoof()
    {
        SpriteRenderer sprite = sprites.Count > 0 ? sprites[0] : null;
        int sortingLayer = sprite != null ? sprite.sortingLayerID : 0;
        int sortingOrder = sprite != null ? sprite.sortingOrder + 10 : 20;
        ComicParticleBurst.Play(effectMaterial, transform.position, Vector2.up, false,
            poofColor, poofColor, new Color(1f, 1f, 1f, 1f), 12, 0, 1.6f, sortingLayer, sortingOrder);
    }

    private void SetSpriteColors(Color tint, bool multiply)
    {
        for (int i = 0; i < sprites.Count; i++)
        {
            if (sprites[i] != null)
                sprites[i].color = multiply ? baseColors[i] * tint : tint;
        }
    }
}
