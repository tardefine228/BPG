using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public sealed class ProjectileComicEffects : MonoBehaviour
{
    [Header("Материал — шейдер BPG/ComicFX")]
    [SerializeField] private Material comicMaterial;

    [Header("Короткий трассер")]
    [SerializeField, Min(0.01f)] private float trailLifetime = 0.12f;
    [SerializeField, Min(0.1f)] private float maxTrailLength = 5f;
    [SerializeField, Min(0.01f)] private float trailWidth = 0.38f;
    [SerializeField, Min(0f)] private float outlineWidth = 0.13f;
    [SerializeField, Min(0f)] private float tailOffset = 0.35f;
    [SerializeField] private Color trailColor = new Color(1f, 0.91f, 0.4f, 1f);
    [SerializeField] private Color inkColor = new Color(0.1f, 0.08f, 0.13f, 0.85f);

    [Header("Попадания")]
    [Tooltip("Ящики определяются по LevelTarget. Эта маска задаёт землю и площадки.")]
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField, Min(0f)] private float minImpactSpeed = 2f;
    [SerializeField, Min(0.02f)] private float impactCooldown = 0.18f;
    [SerializeField, Range(0, 100)] private int dustCount = 9;
    [SerializeField, Range(0, 100)] private int chipCount = 7;
    [SerializeField, Min(0.05f)] private float impactScale = 1.5f;
    [SerializeField] private Color groundDust = new Color(0.88f, 0.74f, 0.56f, 1f);
    [SerializeField] private Color boxDust = new Color(0.95f, 0.85f, 0.66f, 1f);
    [SerializeField] private Color woodChips = new Color(0.76f, 0.43f, 0.2f, 1f);
    [SerializeField] private Color flashColor = new Color(1f, 0.95f, 0.5f, 1f);
    [SerializeField] private int effectsSortingOrder = 10;

    [Header("Способности — только визуальный отклик")]
    [SerializeField] private bool abilityEffects = true;
    [Tooltip("Общий размер вспышек относительно радиуса видимого спрайта кота, с учётом масштаба префаба.")]
    [SerializeField, Min(0.05f)] private float abilityEffectScale = 1.45f;
    [Tooltip("Прозрачность вспышек, колец и частиц способностей. 1 — полная выразительность.")]
    [SerializeField, Range(0f, 1.5f)] private float abilityIntensity = 1f;
    [Tooltip("Длительность светового импульса в игровых секундах. Эффект замирает на паузе.")]
    [SerializeField, Range(0.2f, 1.5f)] private float abilityPulseDuration = 0.72f;
    [Tooltip("Толщина колец, молний и штрихов относительно размера вспышки.")]
    [SerializeField, Range(0.03f, 0.3f)] private float abilityStrokeWidth = 0.1f;
    [SerializeField, Range(0.25f, 3f)] private float splitEffectScale = 0.65f;
    [Tooltip("Длительность небольшой вспышки разделения.")]
    [SerializeField, Range(0.2f, 1f)] private float splitPulseDuration = 0.42f;
    [Tooltip("Ширина голубого следа копий относительно обычного следа.")]
    [SerializeField, Range(1f, 2f)] private float splitTrailWidthMultiplier = 1.35f;
    [Tooltip("Размер компактной вспышки ускорения относительно общего размера эффектов.")]
    [SerializeField, Range(0.25f, 3f)] private float speedEffectScale = 0.62f;
    [Tooltip("Короткий импульс ускорения; отдельная настройка не меняет другие способности.")]
    [SerializeField, Range(0.2f, 1f)] private float speedPulseDuration = 0.32f;
    [SerializeField, Range(0.25f, 3f)] private float blastEffectScale = 1.1f;
    [SerializeField, Range(0.25f, 3f)] private float inflateEffectScale = 1f;
    [Tooltip("Сколько секунд усиленный след остаётся после способности. Разделённые копии сохраняют голубой след до приземления.")]
    [SerializeField, Range(0.2f, 2f)] private float abilityTrailDuration = 1f;
    [SerializeField, Range(1f, 4f)] private float abilityTrailWidthMultiplier = 2.1f;
    [Tooltip("Сколько секунд золотой след подчёркивает ускорение.")]
    [SerializeField, Range(0.2f, 2f)] private float speedTrailDuration = 0.45f;
    [SerializeField, Range(1f, 2f)] private float speedTrailWidthMultiplier = 1.25f;
    [SerializeField] private Color splitColor = new Color(0.3f, 0.9f, 1f, 1f);
    [SerializeField] private Color speedColor = new Color(1f, 0.8f, 0.2f, 1f);
    [SerializeField] private Color blastColor = new Color(1f, 0.45f, 0.12f, 1f);
    [SerializeField] private Color inflateColor = new Color(1f, 0.83f, 0.6f, 1f);

    private readonly Dictionary<Collider2D, float> lastImpacts = new Dictionary<Collider2D, float>();
    private Rigidbody2D body;
    private SpriteRenderer projectileSprite;
    [SerializeField] private TrailRenderer outline;
    [SerializeField] private TrailRenderer ribbon;
    [SerializeField] private TrailRenderer core;
    [SerializeField] private GameObject trailRoot;
    private bool launched;
    private bool trailStopped;
    private float trailFadeRemaining;
    private int sortingLayer;
    private float abilityTrailRemaining;
    private bool splitTrailActive;
    private bool compactAbilityTrail;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        projectileSprite = GetComponentInChildren<SpriteRenderer>();
        sortingLayer = projectileSprite != null ? projectileSprite.sortingLayerID : 0;
        if (comicMaterial == null)
        {
            Debug.LogWarning("ProjectileComicEffects: назначь материал с шейдером BPG/ComicFX.", this);
            return;
        }

        ConfigureTrail(outline, trailWidth + outlineWidth * 2f, inkColor, effectsSortingOrder);
        ConfigureTrail(ribbon, trailWidth, trailColor, effectsSortingOrder + 1);
        ConfigureTrail(core, trailWidth * 0.28f, Color.white, effectsSortingOrder + 2);
    }

    private void ConfigureTrail(TrailRenderer trail, float width, Color color, int order)
    {
        trail.emitting = false;
        trail.sharedMaterial = comicMaterial;
        trail.time = trailLifetime;
        trail.minVertexDistance = 0.08f;
        trail.widthMultiplier = width;
        trail.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        trail.numCapVertices = 4;
        trail.numCornerVertices = 3;
        trail.alignment = LineAlignment.View;
        trail.sortingLayerID = sortingLayer;
        trail.sortingOrder = order;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = gradient;
    }

    // Включает след только при фактическом запуске снаряда.
    public void BeginFlight()
    {
        RestoreTrailStyle();
        launched = true;
        trailStopped = false;
        lastImpacts.Clear();
        if (trailRoot == null)
            return;
        MoveTrail();
        outline.Clear();
        ribbon.Clear();
        core.Clear();
        outline.emitting = ribbon.emitting = core.emitting = true;
    }

    private void LateUpdate()
    {
        if (abilityTrailRemaining > 0f && !splitTrailActive)
        {
            abilityTrailRemaining -= Time.deltaTime;
            if (abilityTrailRemaining <= 0f) RestoreTrailStyle();
        }
        if (!launched || trailRoot == null)
            return;
        if (trailStopped)
        {
            // Убирает остаток следа, даже если снаряд продолжает лежать на земле.
            trailFadeRemaining -= Time.deltaTime;
            if (trailFadeRemaining <= 0f)
            {
                outline.Clear();
                ribbon.Clear();
                core.Clear();
            }
            return;
        }
        MoveTrail();
    }

    private void MoveTrail()
    {
        Vector2 velocity = body.linearVelocity;
        Vector2 direction = velocity.sqrMagnitude > 0.001f ? velocity.normalized : Vector2.right;
        trailRoot.transform.position = transform.position - (Vector3)(direction * tailOffset);
        bool highlighted = abilityTrailRemaining > 0f;
        float lifetime = highlighted ? trailLifetime * (compactAbilityTrail ? 1.35f : 2.6f) : trailLifetime;
        float length = highlighted && !compactAbilityTrail
            ? Mathf.Max(maxTrailLength, VisibleRadius() * 7f) : maxTrailLength;
        float duration = Mathf.Min(lifetime, length / Mathf.Max(0.1f, velocity.magnitude));
        outline.time = ribbon.time = core.time = Mathf.Max(0.01f, duration);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!launched || collision.collider == null)
            return;
        StopTrail();
        if (comicMaterial == null || collision.relativeVelocity.sqrMagnitude < minImpactSpeed * minImpactSpeed)
            return;
        if (collision.collider.GetComponentInParent<ProjectileImpactReporter>() != null)
            return;

        bool boxHit = collision.collider.GetComponentInParent<LevelTarget>() != null;
        if (!boxHit && (groundLayers.value & (1 << collision.collider.gameObject.layer)) == 0)
            return;
        Collider2D hit = collision.collider;
        if (lastImpacts.TryGetValue(hit, out float lastTime) && Time.time - lastTime < impactCooldown)
            return;
        lastImpacts[hit] = Time.time;
        Vector2 point = collision.contactCount > 0 ? collision.GetContact(0).point : (Vector2)transform.position;
        Vector2 normal = collision.contactCount > 0 ? collision.GetContact(0).normal : Vector2.up;
        ComicParticleBurst.Play(comicMaterial, new Vector3(point.x, point.y, transform.position.z),
            normal, boxHit, boxHit ? boxDust : groundDust, woodChips, flashColor,
            dustCount, chipCount, impactScale, sortingLayer, effectsSortingOrder + 3);
    }

    public void EndFlight() => StopTrail();

    public void PlayAbilityEffect(ComicParticleBurst.AbilityEffect effect, float radius = 0f)
    {
        if (!abilityEffects || comicMaterial == null || abilityIntensity <= 0f) return;
        Color color = effect == ComicParticleBurst.AbilityEffect.Split ? splitColor
            : effect == ComicParticleBurst.AbilityEffect.Speed ? speedColor
            : effect == ComicParticleBurst.AbilityEffect.Blast ? blastColor : inflateColor;
        Vector2 direction = body.linearVelocity.sqrMagnitude > 0.001f
            ? body.linearVelocity.normalized : Vector2.right;
        float individualScale = effect == ComicParticleBurst.AbilityEffect.Split ? splitEffectScale
            : effect == ComicParticleBurst.AbilityEffect.Speed ? speedEffectScale
            : effect == ComicParticleBurst.AbilityEffect.Blast ? blastEffectScale : inflateEffectScale;
        float scale = VisibleRadius() * abilityEffectScale * individualScale;
        Vector3 origin = projectileSprite != null ? projectileSprite.bounds.center : transform.position;
        int order = Mathf.Max(effectsSortingOrder + 3,
            projectileSprite != null ? projectileSprite.sortingOrder + 2 : effectsSortingOrder + 3);
        // Кольцо раздувания и скоростные штрихи сопровождают кота. Вспышки
        // разделения и взрыва остаются в точке активации в общем пуле.
        Transform follow = effect == ComicParticleBurst.AbilityEffect.Speed
            || effect == ComicParticleBurst.AbilityEffect.Inflate ? transform : null;
        bool speed = effect == ComicParticleBurst.AbilityEffect.Speed;
        bool split = effect == ComicParticleBurst.AbilityEffect.Split;
        if (ComicEffectsPool.Instance != null)
            ComicEffectsPool.Instance.Next().BeginAbility(origin, direction, effect,
                color, scale, radius, sortingLayer, order, comicMaterial,
                speed ? speedPulseDuration : split ? splitPulseDuration : abilityPulseDuration,
                abilityIntensity, abilityStrokeWidth, follow);
        HighlightAbilityTrail(color, speed ? speedTrailDuration : abilityTrailDuration, speed, split);
    }

    // Копии получают голубой след, а вспышка разделения создаётся только один раз.
    public void HighlightSplitTrail()
    {
        HighlightAbilityTrail(splitColor, abilityTrailDuration, split: true);
        splitTrailActive = abilityTrailRemaining > 0f;
    }

    private float VisibleRadius()
    {
        if (projectileSprite == null) return 1f;
        Vector3 extents = projectileSprite.bounds.extents;
        return Mathf.Max(0.15f, Mathf.Max(extents.x, extents.y));
    }

    private void HighlightAbilityTrail(Color color, float duration, bool speed = false, bool split = false)
    {
        if (!abilityEffects || comicMaterial == null || trailRoot == null || trailStopped || abilityIntensity <= 0f) return;
        splitTrailActive = false;
        compactAbilityTrail = speed || split;
        abilityTrailRemaining = duration;
        float width = speed ? trailWidth * speedTrailWidthMultiplier
            : split ? trailWidth * splitTrailWidthMultiplier
            : Mathf.Max(trailWidth, VisibleRadius() * 0.2f) * abilityTrailWidthMultiplier;
        float opacity = Mathf.Clamp01(abilityIntensity) * (split ? 0.75f : 1f);
        Color vivid = color;
        vivid.a *= opacity;
        ConfigureTrail(outline, width + outlineWidth * 2f, inkColor, effectsSortingOrder);
        ConfigureTrail(ribbon, width, vivid, effectsSortingOrder + 1);
        ConfigureTrail(core, width * 0.32f,
            new Color(1f, 1f, 1f, opacity), effectsSortingOrder + 2);
        outline.emitting = ribbon.emitting = core.emitting = launched;
    }

    private void RestoreTrailStyle()
    {
        abilityTrailRemaining = 0f;
        splitTrailActive = false;
        compactAbilityTrail = false;
        if (comicMaterial == null || trailRoot == null) return;
        ConfigureTrail(outline, trailWidth + outlineWidth * 2f, inkColor, effectsSortingOrder);
        ConfigureTrail(ribbon, trailWidth, trailColor, effectsSortingOrder + 1);
        ConfigureTrail(core, trailWidth * 0.28f, Color.white, effectsSortingOrder + 2);
        outline.emitting = ribbon.emitting = core.emitting = launched && !trailStopped;
    }

    private void StopTrail()
    {
        if (trailStopped)
            return;
        trailStopped = true;
        trailFadeRemaining = abilityTrailRemaining > 0f
            ? trailLifetime * (compactAbilityTrail ? 1.35f : 2.6f) : trailLifetime;
        if (outline != null)
            outline.emitting = ribbon.emitting = core.emitting = false;
    }

}
