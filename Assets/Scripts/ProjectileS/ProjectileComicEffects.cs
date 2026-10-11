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

    private readonly Dictionary<Collider2D, float> lastImpacts = new Dictionary<Collider2D, float>();
    private Rigidbody2D body;
    [SerializeField] private TrailRenderer outline;
    [SerializeField] private TrailRenderer ribbon;
    [SerializeField] private TrailRenderer core;
    [SerializeField] private GameObject trailRoot;
    private bool launched;
    private bool trailStopped;
    private float trailFadeRemaining;
    private int sortingLayer;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
        sortingLayer = sprite != null ? sprite.sortingLayerID : 0;
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
        float duration = Mathf.Min(trailLifetime, maxTrailLength / Mathf.Max(0.1f, velocity.magnitude));
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

    private void StopTrail()
    {
        if (trailStopped)
            return;
        trailStopped = true;
        trailFadeRemaining = trailLifetime;
        if (outline != null)
            outline.emitting = ribbon.emitting = core.emitting = false;
    }

}
