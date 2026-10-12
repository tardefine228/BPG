using System;
using System.Collections;
using UnityEngine;

// Пол и выпущенный кот убивают мышь сразу; прочие столкновения наносят урон.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public sealed class MouseEnemy : MonoBehaviour
{
    [Header("Роль на уровне")]
    [Tooltip("Если включено, эту мышь обязательно убить для победы.")]
    [SerializeField] private bool countsForVictory = true;
    public bool CountsForVictory => countsForVictory;

    [Header("Здоровье")]
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    [Header("Урон от столкновений")]
    [Tooltip("Удары медленнее этого порога не наносят урон и не дают очки.")]
    [SerializeField, Min(0f)] private float minImpactSpeed = 2f;
    [Tooltip("Чем выше скорость столкновения, тем больше урон.")]
    [SerializeField, Min(0f)] private float damageMultiplier = 2f;
    [Tooltip("Защита от многократного урона за одно столкновение.")]
    [SerializeField, Min(0f)] private float hitCooldown = 0.15f;

    [Header("Смерть")]
    [Tooltip("Мышь погибает, если проваливается ниже этой высоты (за пределы уровня).")]
    [SerializeField] private float fallY = -6f;
    [Header("Отлёт при гибели")]
    [SerializeField] private Sprite deathSprite;
    [SerializeField, Range(0.2f, 2f)] private float deathDuration = 0.85f;
    [SerializeField, Min(0f)] private float deathHopSpeed = 10f;
    [SerializeField, Min(0f)] private float deathSidewaysSpeed = 5f;
    [SerializeField, Min(0f)] private float deathGravity = 28f;
    [SerializeField, Min(0f)] private float deathSpinSpeed = 420f;
    public bool IsDeathAnimating { get; private set; }

    [Header("Контакт с полом и снарядом")]
    [Tooltip("Коллайдер земли (плотный Layer1 у Tilemap). Пустое поле берёт Terrain Collider камеры.")]
    [SerializeField] private Collider2D floor;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }

    // LevelSession начисляет очки за фактический урон и за смерть.
    public event Action<MouseEnemy, float> Damaged;
    public event Action<MouseEnemy> Died;

    private float nextHitTime;

    private void Awake()
    {
        Health = Mathf.Max(1f, maxHealth);
    }

    private void Start()
    {
        if (floor == null && Camera.main != null)
        {
            var camera = Camera.main.GetComponent<GameplayCameraController>();
            if (camera != null)
                floor = camera.TerrainCollider;
        }
    }

    private void Update()
    {
        // У ступенчатой карты бывают площадки ниже старой отметки -6.
        // Падение за карту считается только ниже всех плотных блоков Tilemap.
        float cutoff = floor is UnityEngine.Tilemaps.TilemapCollider2D
            ? Mathf.Min(fallY, floor.bounds.min.y - 4f) : fallY;
        if (!IsDead && transform.position.y <= cutoff)
            Kill();
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (!IsDead && collision.collider == floor)
            Kill();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsDead || collision == null)
            return;

        if (collision.collider == floor)
        {
            Kill();
            return;
        }
        if (collision.collider.GetComponentInParent<CatProjectile>() != null
            || collision.collider.GetComponentInParent<ProjectileImpactReporter>() != null)
        {
            TakeDamage(Health);
            return;
        }

        if (Time.time < nextHitTime)
            return;

        float speed = collision.relativeVelocity.magnitude;
        if (speed <= minImpactSpeed)
            return;

        float damage = (speed - minImpactSpeed) * damageMultiplier;
        if (damage <= 0f)
            return;

        nextHitTime = Time.time + hitCooldown;
        TakeDamage(damage);
    }

    public void TakeDamage(float damage)
    {
        if (IsDead || damage <= 0f)
            return;

        float actualDamage = Mathf.Min(Health, damage);
        Health -= actualDamage;
        Damaged?.Invoke(this, actualDamage);

        if (Health <= 0f)
            Kill();
    }

    private void Kill()
    {
        if (IsDead)
            return;

        IsDead = true;
        Health = 0f;
        // Флаг, столкновения и новая поза меняются в тот же кадр касания.
        StartCoroutine(DeathRoutine());
        Died?.Invoke(this);
    }

    private IEnumerator DeathRoutine()
    {
        IsDeathAnimating = true;
        Rigidbody2D body = GetComponent<Rigidbody2D>();
        float direction = body != null && Mathf.Abs(body.linearVelocity.x) > 0.1f
            ? Mathf.Sign(body.linearVelocity.x) : (UnityEngine.Random.value < 0.5f ? -1f : 1f);
        foreach (Collider2D hitbox in GetComponentsInChildren<Collider2D>())
            hitbox.enabled = false;
        if (body != null)
            body.simulated = false;

        SpriteRenderer sprite = GetComponent<SpriteRenderer>();
        if (sprite != null)
        {
            if (deathSprite != null)
                sprite.sprite = deathSprite;
            sprite.sortingOrder = 1000;
            if (ComicEffectsPool.Instance != null)
                ComicEffectsPool.Instance.Next().Begin(transform.position, Vector2.up, false,
                    new Color(0.94f, 0.9f, 0.82f, 0.8f), Color.clear,
                    new Color(1f, 0.96f, 0.7f, 0.9f), 12, 0, 1f,
                    sprite.sortingLayerID, sprite.sortingOrder + 1);
        }
        Vector3 start = transform.position;
        Vector3 scale = transform.localScale;
        Color color = sprite != null ? sprite.color : Color.white;
        float elapsed = 0f;
        while (elapsed < deathDuration)
        {
            if (Time.timeScale <= 0f)
            {
                yield return null;
                continue;
            }
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.2f, deathDuration));
            transform.position = start + new Vector3(direction * deathSidewaysSpeed * elapsed,
                deathHopSpeed * elapsed - 0.5f * deathGravity * elapsed * elapsed, 0f);
            transform.Rotate(0f, 0f, direction * deathSpinSpeed * Time.deltaTime);
            transform.localScale = scale * (1f + 0.18f * Mathf.Sin(t * Mathf.PI))
                * Mathf.Lerp(1f, 0.65f, t * t);
            if (sprite != null)
                sprite.color = new Color(color.r, color.g, color.b,
                    color.a * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, t))));
            yield return null;
        }
        foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>())
            renderer.enabled = false;
        IsDeathAnimating = false;
        // Объект остаётся для ссылок LevelSession, физика уже выключена.
    }
}
