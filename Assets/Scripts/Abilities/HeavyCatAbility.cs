using System.Collections;
using UnityEngine;

// В полёте кот становится тяжелее и визуально раздувается: по ЛКМ
// спрайт расширяется (масштаб + радиус коллайдера), масса растёт,
// снаряд сильнее толкает блоки.
public sealed class HeavyCatAbility : CatAbility
{
    [SerializeField, Min(1f)] private float massMultiplier = 4f;
    [SerializeField, Min(0f)] private float forwardImpulse = 3f;

    [Header("Расширяющийся спрайт")]
    [Tooltip("Спрайт \"раздутого\" кота из атласа. Если не назначен, остаётся текущий спрайт.")]
    [SerializeField] private Sprite expandedSprite;
    [Tooltip("За сколько секунд спрайт и коллайдер достигают полного размера.")]
    [SerializeField, Min(0.05f)] private float expandDuration = 0.25f;
    [SerializeField, Min(1f)] private float visualScaleMultiplier = 1.6f;

    private SpriteRenderer spriteRenderer;
    private Collider2D hitbox;
    private CircleCollider2D circleHitbox;
    private BoxCollider2D boxHitbox;
    private Vector3 baseLocalScale;
    private Vector2 baseColliderSize;
    private float baseColliderRadius;
    private bool expanding;

    protected override void Awake()
    {
        base.Awake();
        // Спрайт кота живёт на корне префаба вместе с Rigidbody2D.
        spriteRenderer = GetComponent<SpriteRenderer>();
        hitbox = GetComponent<Collider2D>();
        circleHitbox = hitbox as CircleCollider2D;
        boxHitbox = hitbox as BoxCollider2D;
        baseLocalScale = transform.localScale;
        if (boxHitbox != null)
            baseColliderSize = boxHitbox.size;
        if (circleHitbox != null)
            baseColliderRadius = circleHitbox.radius;
    }

    protected override void Activate()
    {
        GetComponent<ProjectileComicEffects>()?.PlayAbilityEffect(ComicParticleBurst.AbilityEffect.Inflate);
        Body.mass *= massMultiplier;
        if (Body.linearVelocity.sqrMagnitude > 0.01f)
            Body.AddForce(Body.linearVelocity.normalized * forwardImpulse, ForceMode2D.Impulse);

        // Расширение запускается только после настоящего выстрела, когда
        // Rigidbody стал динамическим (копии в очереди не раздуваются).
        if (!expanding && Body.bodyType == RigidbodyType2D.Dynamic)
        {
            expanding = true;
            StartCoroutine(ExpandRoutine());
        }
    }

    private IEnumerator ExpandRoutine()
    {
        // Плавный рост за expandDuration секунд реального игрового времени.
        float elapsed = 0f;
        while (elapsed < expandDuration)
        {
            elapsed += Time.deltaTime;
            ApplyExpansion(Mathf.Clamp01(elapsed / expandDuration));
            yield return null;
        }
        ApplyExpansion(1f);
    }

    private void ApplyExpansion(float t)
    {
        float growth = Mathf.Lerp(1f, visualScaleMultiplier, t);

        if (spriteRenderer != null)
        {
            if (expandedSprite != null && t >= 1f)
                spriteRenderer.sprite = expandedSprite;
            // Раздуваем сам Transform: спрайт, коллайдер и эффекты растут вместе.
            transform.localScale = baseLocalScale * growth;
        }

        // Коллайдер не должен увеличиваться дважды (он наследует масштаб
        // трансформа), поэтому его размеры остаются базовыми.
    }
}
