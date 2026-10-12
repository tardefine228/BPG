using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// Цель уровня: обычный блок сбивается одним сильным ударом, бронированный выдерживает несколько. Прочность и сопротивление настраиваются в Inspector.
public sealed class LevelTarget : MonoBehaviour
{
    private const float IgnoreCollisionsFor = 0.8f;

    // Сколько секунд после контакта сохраняется дополнительное трение.
    private const float SurfaceContactGrace = 0.12f;

    [Header("Роль на уровне")]
    [Tooltip("Включи, если эту цель ОБЯЗАТЕЛЬНО нужно уничтожить для победы. Отключи для препятствий и декоративных коробок.")]
    [SerializeField] private bool countsForVictory = true;
    public bool CountsForVictory => countsForVictory;

    [Header("Сбивание")]
    [Tooltip("Блок считается сбитым, если упал ниже этой отметки.")]
    [SerializeField] private float fallY = -6f;

    [Tooltip("Минимальная скорость столкновения, при которой удар вообще засчитывается.")]
    [SerializeField, Min(0f)] private float impactSpeed = 2f;

    [Header("Прочность блока")]
    [Tooltip("Сколько ударов выдерживает блок. 1 — обычный блок (как в 1 уровне). " +
             "3–6 — бронированный: держит несколько попаданий и сдвигается от них " +
             "слабее обычного (см. «Сопротивление сдвигу» ниже).")]
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

    [Header("Сопротивление сдвигу")]
    [Tooltip("Какая доля толчка гасится в момент попадания. 0 — обычный блок отлетает как есть, " +
             "0.6 — бронированный заметно тяжелее и только подаётся назад, " +
             "0.9 — почти не сдвинуть.")]
    [SerializeField, Range(0f, 0.95f)] private float pushResistance;

    [Tooltip("Дополнительное трение: во сколько раз сильнее блок тормозит, скользя по поверхности. " +
             "1 — как обычный блок, 1.8 — тяжело скользит и быстро останавливается.")]
    [SerializeField, Min(1f)] private float slidingFriction = 1f;

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
    private float surfaceContactUntil;
    private Coroutine flashRoutine;

    // Срабатывает при каждом засчитанном сильном ударе, включая последний.
    public event Action<LevelTarget, float> HitRegistered;
    // Одно событие за фактическое уничтожение цели (включая падение за границу).
    public event Action<LevelTarget> Cleared;

    public bool IsCleared { get; private set; }
    public float MaxTravelDistance { get; private set; }

    // Число уже полученных ударов.
    public int HitsTaken { get; private set; }

    // Прочность блока; для обычного блока равна одному удару.
    public int HitsToClear => Mathf.Max(1, hitsToClear);

    // Оставшееся число ударов до сбивания.
    public int HitsRemaining => Mathf.Max(0, HitsToClear - HitsTaken);

    // Доля погашенного толчка; обычный блок не поглощает удар.
    public float PushResistance => Mathf.Clamp(pushResistance, 0f, 0.95f);

    // Множитель дополнительного трения при скольжении.
    public float SlidingFriction => Mathf.Max(1f, slidingFriction);

    // Признак бронированного блока.
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
    }

    private void Update()
    {
        float distance = Vector2.Distance(transform.position, startPosition);
        if (distance > MaxTravelDistance)
        {
            MaxTravelDistance = distance;
        }

        if (IsCleared)
        {
            return;
        }

        if (transform.position.y <= fallY)
        {
            PlayBoxSound();
            ClearTarget(Vector2.zero, false);
        }
    }

    private void PlayBoxSound()
    {
        AudioClip clip = Resources.Load<AudioClip>("Audio/music for box/falling-boards");
        if (clip != null && AudioManager.Instance != null)
        {
            GameObject soundObj = new GameObject("BoxFallingSound");
            AudioSource source = soundObj.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = AudioManager.Instance.EffectsVolume;
            source.Play();
            Destroy(soundObj, 1.0f);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsCleared || collision == null || collision.collider == null)
        {
            return;
        }

        if (collision.collider.GetComponentInParent<CatProjectile>() != null || collision.collider.GetComponentInParent<ProjectileImpactReporter>() != null)
        {
            PlayBoxSound();
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

        RegisterHit(relativeVelocity.magnitude, relativeVelocity.normalized);
    }

    // Для взрывов и способностей котов: тот же счётчик прочности,
    // те же события и защита от повторных попаданий.
    public void ApplyAbilityHit(float strength, Vector2 direction)
    {
        if (strength < impactSpeed)
            return;
        RegisterHit(strength, direction);
    }

    private void RegisterHit(float strength, Vector2 direction)
    {
        if (IsCleared || Time.time < nextHitTime)
            return;

        nextHitTime = Time.time + hitCooldown;
        HitsTaken++;
        HitRegistered?.Invoke(this, strength);
        AbsorbHit();

        if (HitsTaken >= HitsToClear)
            ClearTarget(direction, true);
        else
            PlayHitFeedback();
    }

    // Бронированный блок поглощает часть толчка и вращения.
    private void AbsorbHit()
    {
        if (pushResistance <= 0f || body == null || body.bodyType != RigidbodyType2D.Dynamic)
        {
            return;
        }

        float keep = 1f - PushResistance;
        body.linearVelocity *= keep;
        body.angularVelocity *= keep;
    }

    // Дополнительное торможение тяжёлого блока на поверхности.
    private void FixedUpdate()
    {
        float extraFriction = SlidingFriction - 1f;
        if (extraFriction <= 0.0001f || body == null || IsCleared)
        {
            return;
        }

        if (body.bodyType != RigidbodyType2D.Dynamic || Time.time > surfaceContactUntil)
        {
            return;
        }

        Vector2 velocity = body.linearVelocity;
        if (Mathf.Abs(velocity.x) < 0.01f)
        {
            return;
        }

        // Торможение имитирует дополнительное сухое трение.
        float deceleration = extraFriction * Mathf.Abs(Physics2D.gravity.y)
            * Time.fixedDeltaTime;
        body.linearVelocity = new Vector2(
            Mathf.MoveTowards(velocity.x, 0f, deceleration), velocity.y);
    }

    // Контакт с поверхностью продлевает действие дополнительного трения.
    private void OnCollisionStay2D(Collision2D collision)
    {
        if (IsCleared || collision == null)
        {
            return;
        }

        surfaceContactUntil = Time.time + SurfaceContactGrace;
    }

    private void PlayHitFeedback()
    {
        ApplyDamageTint();

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

        if (byImpact && breakOnCleared && hitDirection.sqrMagnitude > 0.0001f)
        {
            BreakApart(hitDirection);
        }

        onCleared.Invoke();
        Cleared?.Invoke(this);
    }

    // Отбрасывает сбитый блок. Коллайдер и компонент сохраняются для подсчёта целей и физики.
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
}
