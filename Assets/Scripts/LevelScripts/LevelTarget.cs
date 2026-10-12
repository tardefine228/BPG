using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class LevelTarget : MonoBehaviour
{
    private const float IgnoreCollisionsFor = 0.8f;
    private const float SurfaceContactGrace = 0.12f;

    [Header("Роль на уровне")]
    [SerializeField] private bool countsForVictory = true;
    public bool CountsForVictory => countsForVictory;

    [Header("Сбивание")]
    [SerializeField] private float fallY = -6f;
    [SerializeField, Min(0f)] private float impactSpeed = 2f;

    [Header("Прочность блока")]
    [SerializeField, Min(1)] private int hitsToClear = 1;
    [SerializeField, Min(0f)] private float hitCooldown = 0.35f;

    [Header("Отклик на удары")]
    [SerializeField] private bool showHitFeedback = true;
    [SerializeField] private Color damagedTint =
        new Color(0.32f, 0.31f, 0.36f, 1f);
    [SerializeField] private Color hitFlashColor =
        new Color(1f, 0.85f, 0.4f, 1f);
    [SerializeField, Min(0f)] private float hitFlashDuration = 0.12f;

    [Header("Развал после последнего удара")]
    [SerializeField] private bool breakOnCleared;
    [SerializeField, Min(0f)] private float breakImpulse = 6f;
    [SerializeField, Min(0f)] private float breakTorque = 30f;
    [SerializeField] private bool destroyOnCleared;
    [SerializeField, Min(0f)] private float destroyDelay = 0.4f;
    [SerializeField] private GameObject debrisPrefab;
    [SerializeField] private bool shatterIntoDebris;

    [Header("Сопротивление сдвигу")]
    [SerializeField, Range(0f, 0.95f)] private float pushResistance;
    [SerializeField, Min(1f)] private float slidingFriction = 1f;

    [Header("События")]
    [SerializeField] private UnityEvent onHit = new UnityEvent();
    [SerializeField] private UnityEvent onCleared = new UnityEvent();

    private readonly List<SpriteRenderer> damageSprites =
        new List<SpriteRenderer>();

    private Color[] baseColors = Array.Empty<Color>();
    private Rigidbody2D body;
    private Vector3 startPosition;
    private float nextHitTime;
    private float surfaceContactUntil;
    private Coroutine flashRoutine;
    private bool soundPlayedForCurrentFall;

    public event Action<LevelTarget, float> HitRegistered;
    public event Action<LevelTarget> Cleared;

    public bool IsCleared { get; private set; }
    public float MaxTravelDistance { get; private set; }
    public int HitsTaken { get; private set; }

    public int HitsToClear => Mathf.Max(1, hitsToClear);
    public int HitsRemaining => Mathf.Max(0, HitsToClear - HitsTaken);
    public float PushResistance => Mathf.Clamp(pushResistance, 0f, 0.95f);
    public float SlidingFriction => Mathf.Max(1f, slidingFriction);
    public bool IsReinforced => HitsToClear > 1;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();

        damageSprites.AddRange(
            GetComponentsInChildren<SpriteRenderer>(true));

        baseColors = new Color[damageSprites.Count];

        for (int i = 0; i < damageSprites.Count; i++)
        {
            if (damageSprites[i] != null)
                baseColors[i] = damageSprites[i].color;
        }

        startPosition = transform.position;
        ApplyDamageTint();
    }

    private void Update()
    {
        float distance = Vector2.Distance(
            transform.position, startPosition);

        if (distance > MaxTravelDistance)
            MaxTravelDistance = distance;

        if (!IsCleared && transform.position.y <= fallY)
        {
            PlayBoxSound();
            ClearTarget(Vector2.zero, false);
        }
    }

    private void PlayBoxSound()
    {
        if (Time.time < nextBoxSoundTime) return;
        AudioClip clip = Resources.Load<AudioClip>("Audio/music for box/falling-objects-with-a-crash");
        if (clip != null && AudioManager.Instance != null)
        {
            nextBoxSoundTime = Time.time + 0.15f;
            AudioManager.Instance.PlaySfx(clip);
        }
    }

    private float nextBoxSoundTime;

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsCleared || collision == null || collision.collider == null)
            return;

        if (Time.timeSinceLevelLoad < IgnoreCollisionsFor)
            return;

        LevelTarget otherTarget =
            collision.collider.GetComponentInParent<LevelTarget>();

        if (otherTarget != null && otherTarget != this)
            return;

        if (collision.rigidbody != null &&
            collision.rigidbody.bodyType == RigidbodyType2D.Static)
            return;

        Vector2 relativeVelocity = collision.relativeVelocity;
        float strength = relativeVelocity.magnitude;

        if (strength < impactSpeed)
            return;

        bool projectileContact =
            collision.collider.GetComponentInParent<CatProjectile>() != null ||
            collision.collider.GetComponentInParent<ProjectileImpactReporter>() != null;

        if (projectileContact)
            PlayBoxSound();

        Vector2 direction = relativeVelocity.sqrMagnitude > 0.0001f
            ? relativeVelocity.normalized
            : Vector2.zero;

        RegisterHit(strength, direction);
    }

    public void ApplyAbilityHit(float strength, Vector2 direction)
    {
        if (IsCleared || strength < impactSpeed)
            return;

        if (direction.sqrMagnitude > 0.0001f)
            direction.Normalize();

        RegisterHit(strength, direction);
    }

    private void RegisterHit(float strength, Vector2 direction)
    {
        if (IsCleared || Time.time < nextHitTime)
            return;

        nextHitTime = Time.time + hitCooldown;
        PlayBoxSound();
        HitsTaken++;

        HitRegistered?.Invoke(this, strength);
        AbsorbHit();

        if (HitsTaken >= HitsToClear)
            ClearTarget(direction, true);
        else
            PlayHitFeedback();
    }

    private void AbsorbHit()
    {
        if (body == null ||
            body.bodyType != RigidbodyType2D.Dynamic ||
            PushResistance <= 0f)
            return;

        float keep = 1f - PushResistance;
        body.linearVelocity *= keep;
        body.angularVelocity *= keep;
    }

    private void FixedUpdate()
    {
        float extraFriction = SlidingFriction - 1f;

        if (extraFriction <= 0.0001f ||
            body == null ||
            IsCleared ||
            body.bodyType != RigidbodyType2D.Dynamic ||
            Time.time > surfaceContactUntil)
            return;

        Vector2 velocity = body.linearVelocity;

        if (Mathf.Abs(velocity.x) < 0.01f)
            return;

        float deceleration = extraFriction *
            Mathf.Abs(Physics2D.gravity.y) *
            Time.fixedDeltaTime;

        body.linearVelocity = new Vector2(
            Mathf.MoveTowards(velocity.x, 0f, deceleration),
            velocity.y);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (IsCleared || collision == null)
            return;

        surfaceContactUntil = Time.time + SurfaceContactGrace;
    }

    private void PlayHitFeedback()
    {
        ApplyDamageTint();

        if (IsReinforced && showHitFeedback &&
            damageSprites.Count > 0)
        {
            if (flashRoutine != null)
                StopCoroutine(flashRoutine);

            flashRoutine = StartCoroutine(FlashRoutine());
        }

        onHit.Invoke();
    }

    private IEnumerator FlashRoutine()
    {
        SetSpriteColors(hitFlashColor);

        yield return new WaitForSeconds(
            Mathf.Max(0.02f, hitFlashDuration));

        ApplyDamageTint();
        flashRoutine = null;
    }

    private void ClearTarget(Vector2 hitDirection, bool byImpact)
    {
        if (IsCleared)
            return;

        IsCleared = true;

        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }

        ApplyDamageTint();

        bool validDirection =
            hitDirection.sqrMagnitude > 0.0001f;

        bool shatter =
            shatterIntoDebris && byImpact && validDirection;

        if (byImpact && !shatter &&
            breakOnCleared && validDirection)
        {
            BreakApart(hitDirection);
        }

        onCleared.Invoke();
        Cleared?.Invoke(this);

        if (shatter)
            Shatter(hitDirection);

        if (destroyOnCleared)
            StartCoroutine(DestroyAfterDelay());
    }

    private void Shatter(Vector2 hitDirection)
    {
        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;

        if (debrisPrefab != null)
        {
            GameObject debris = Instantiate(
                debrisPrefab, position, rotation);

            LaunchDebris(debris, hitDirection);
        }
        else
        {
            foreach (SpriteRenderer sprite in
                     GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sprite == null || sprite.sprite == null)
                    continue;

                GameObject piece =
                    new GameObject(name + "_Debris");

                piece.transform.position = sprite.transform.position;
                piece.transform.rotation = sprite.transform.rotation;
                piece.transform.localScale = sprite.transform.lossyScale;

                SpriteRenderer clone =
                    piece.AddComponent<SpriteRenderer>();

                clone.sprite = sprite.sprite;
                clone.color = sprite.color;
                clone.flipX = sprite.flipX;
                clone.flipY = sprite.flipY;
                clone.sortingLayerID = sprite.sortingLayerID;
                clone.sortingOrder = sprite.sortingOrder;
                clone.drawMode = sprite.drawMode;
                clone.size = sprite.size;

                Rigidbody2D pieceBody =
                    piece.AddComponent<Rigidbody2D>();

                pieceBody.gravityScale =
                    body != null ? Mathf.Max(0.1f, body.gravityScale) : 1f;

                LaunchDebris(piece, hitDirection);
            }
        }

        foreach (SpriteRenderer sprite in
                 GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sprite != null)
                sprite.enabled = false;
        }

        if (body != null)
            body.simulated = false;

        foreach (Collider2D collider in
                 GetComponentsInChildren<Collider2D>(true))
        {
            collider.enabled = false;
        }
    }

    private void LaunchDebris(
        GameObject debris, Vector2 hitDirection)
    {
        if (debris == null)
            return;

        Rigidbody2D debrisBody =
            debris.GetComponent<Rigidbody2D>();

        if (debrisBody == null)
            debrisBody = debris.AddComponent<Rigidbody2D>();

        debrisBody.bodyType = RigidbodyType2D.Dynamic;

        float spread = UnityEngine.Random.Range(-0.45f, 0.45f);

        Vector2 direction = (
            hitDirection +
            new Vector2(spread, Mathf.Abs(spread) * 0.6f)
        ).normalized;

        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector2.up;

        debrisBody.AddForce(
            direction * Mathf.Max(1f, breakImpulse),
            ForceMode2D.Impulse);

        debrisBody.AddTorque(
            UnityEngine.Random.Range(-1f, 1f) *
            Mathf.Max(1f, breakTorque),
            ForceMode2D.Impulse);

        Destroy(debris, 4f);
    }

    private IEnumerator DestroyAfterDelay()
    {
        if (destroyDelay > 0f)
            yield return new WaitForSeconds(destroyDelay);

        Destroy(gameObject);
    }

    private void BreakApart(Vector2 hitDirection)
    {
        if (body == null ||
            body.bodyType != RigidbodyType2D.Dynamic)
            return;

        body.AddForce(
            hitDirection.normalized * breakImpulse,
            ForceMode2D.Impulse);

        float spin = hitDirection.x >= 0f ? -1f : 1f;

        body.AddTorque(
            spin * breakTorque,
            ForceMode2D.Impulse);
    }

    private void ApplyDamageTint()
    {
        if (!showHitFeedback || !IsReinforced)
        {
            SetSpriteColors(null);
            return;
        }

        float damage = IsCleared
            ? 1f
            : (float)HitsTaken / HitsToClear;

        for (int i = 0; i < damageSprites.Count; i++)
        {
            SpriteRenderer sprite = damageSprites[i];

            if (sprite == null)
                continue;

            sprite.color = Color.Lerp(
                baseColors[i], damagedTint, damage);
        }
    }

    private void SetSpriteColors(Color? color)
    {
        for (int i = 0; i < damageSprites.Count; i++)
        {
            SpriteRenderer sprite = damageSprites[i];

            if (sprite == null)
                continue;

            sprite.color = color ?? baseColors[i];
        }
    }


}
