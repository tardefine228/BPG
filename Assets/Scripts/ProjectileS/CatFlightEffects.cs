using UnityEngine;

// Шерсть в полёте; после удара кот продолжает отскакивать и катиться как физический снаряд.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(CatProjectile))]
public sealed class CatFlightEffects : MonoBehaviour
{
    [Header("Шерсть в полёте")]
    [SerializeField] private ParticleSystem furPrefab;
    [Tooltip("Цвет шерсти этого вида кота. Не зависит от цвета спрайта и других котов.")]
    [SerializeField] private Color furColor = Color.white;
    [SerializeField, Min(0f)] private float furPerSecond = 28f;
    [SerializeField, Min(0.05f)] private float furLifetime = 0.85f;
    [SerializeField, Min(0.01f)] private float furSize = 0.3f;

    [Header("Жизнь после попадания")]
    [Tooltip("Минимум секунд физики после первого удара, даже если кот уже остановился.")]
    [SerializeField, Min(0f)] private float minimumPostImpactLifetime = 3f;
    [Tooltip("Максимум секунд после первого удара перед исчезновением.")]
    [SerializeField, Min(0.1f)] private float maximumPostImpactLifetime = 8f;
    [SerializeField, Min(0f)] private float settleSpeed = 0.3f;
    [SerializeField, Min(0f)] private float settleAngularSpeed = 20f;
    [SerializeField, Min(0f)] private float settleDuration = 1f;
    [SerializeField, Min(0.05f)] private float fadeDuration = 0.45f;
    [SerializeField, Min(0f)] private float cleanupMargin = 4f;

    public bool HasImpacted { get; private set; }
    public bool IsExiting { get; private set; }
    public bool HasPendingCleanup => flying || IsExiting;
    private Rigidbody2D body;
    private SpriteRenderer sprite;
    private ParticleSystem fur;
    private Camera view;
    private bool flying;
    private Vector2 flightVelocity;
    private Vector3 previousFurPosition;
    private float particlesToEmit;
    private float flightStartedAt;
    private float flightLifetime;
    private float impactElapsed;
    private float settledFor;
    private float fadeElapsed;
    private Color originalColor;
    private float fieldBottom;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        sprite = GetComponent<SpriteRenderer>();
    }

    public void BeginFlight(float mapBottom, float maximumFlightLifetime = 12f)
    {
        flying = true;
        HasImpacted = false;
        IsExiting = false;
        flightStartedAt = Time.time;
        flightLifetime = Mathf.Max(0.1f, maximumFlightLifetime);
        fieldBottom = mapBottom;
        view = Camera.main;
        flightVelocity = body.linearVelocity;
        previousFurPosition = transform.position;
        if (furPrefab == null)
            return;
        fur = Instantiate(furPrefab, transform.position, Quaternion.identity, transform);
        var main = fur.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 128;
        main.startSpeed = 0f;
        main.gravityModifier = 0.12f;
        main.startSize3D = true;
        var emission = fur.emission;
        emission.enabled = false; // Позиции распределяются вдоль пути между кадрами.
        var shape = fur.shape;
        shape.enabled = false;
        var color = fur.colorOverLifetime;
        color.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;
        var size = fur.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));
        var rotation = fur.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
        var renderer = fur.GetComponent<ParticleSystemRenderer>();
        renderer.sortingLayerID = sprite.sortingLayerID;
        renderer.sortingOrder = sprite.sortingOrder - 1;
        fur.Play();
    }

    private void FixedUpdate()
    {
        if (flying && !HasImpacted)
            flightVelocity = body.linearVelocity;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!flying || HasImpacted)
            return;
        HasImpacted = true;
        if (fur != null)
            fur.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        GetComponent<ProjectileComicEffects>()?.EndFlight();
        // Физика и коллайдеры остаются включёнными для следующих ударов.
    }

    private void LateUpdate()
    {
        if (Time.timeScale <= 0f)
            return;
        if (IsExiting)
        {
            fadeElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(fadeElapsed / Mathf.Max(0.05f, fadeDuration));
            sprite.color = new Color(originalColor.r, originalColor.g, originalColor.b,
                originalColor.a * (1f - Mathf.SmoothStep(0f, 1f, t)));
            if (t >= 1f)
                Destroy(gameObject);
            return;
        }
        if (!flying)
            return;

        float bottom = fieldBottom;
        if (view != null && view.orthographic)
            bottom = Mathf.Min(bottom, view.transform.position.y - view.orthographicSize);
        if (transform.position.y < bottom - cleanupMargin)
        {
            Destroy(gameObject);
            return;
        }
        if (HasImpacted)
        {
            impactElapsed += Time.deltaTime;
            bool settled = body.IsSleeping() || (body.linearVelocity.sqrMagnitude <= settleSpeed * settleSpeed
                && Mathf.Abs(body.angularVelocity) <= settleAngularSpeed);
            settledFor = settled ? settledFor + Time.deltaTime : 0f;
            if (impactElapsed >= Mathf.Max(minimumPostImpactLifetime, maximumPostImpactLifetime)
                || (impactElapsed >= minimumPostImpactLifetime && settledFor >= settleDuration))
                BeginExit();
            return;
        }
        if (Time.time - flightStartedAt >= flightLifetime)
        {
            BeginExit();
            return;
        }
        if (fur == null)
            return;
        particlesToEmit += Mathf.Max(0f, furPerSecond) * Time.deltaTime;
        int count = Mathf.Min(8, Mathf.FloorToInt(particlesToEmit));
        particlesToEmit -= count;
        Vector2 backwards = flightVelocity.sqrMagnitude > 0.01f ? -flightVelocity.normalized : Vector2.left;
        for (int i = 0; i < count; i++)
        {
            float scale = furSize * Random.Range(0.7f, 1.2f);
            var particle = new ParticleSystem.EmitParams
            {
                position = Vector3.Lerp(previousFurPosition, transform.position, (i + 1f) / Mathf.Max(1, count))
                    + (Vector3)(Random.insideUnitCircle * 0.2f),
                velocity = (Vector3)(backwards * Random.Range(0.5f, 2f) + Random.insideUnitCircle * 0.8f),
                startSize3D = new Vector3(scale * 0.5f, scale, scale),
                startLifetime = furLifetime * Random.Range(0.75f, 1.2f),
                startColor = furColor,
                rotation = Random.Range(0f, 360f)
            };
            fur.Emit(particle, 1);
        }
        previousFurPosition = transform.position;
    }

    private void BeginExit()
    {
        flying = false;
        IsExiting = true;
        originalColor = sprite.color;
        if (fur != null)
            fur.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        body.simulated = false;
        foreach (var collider in GetComponentsInChildren<Collider2D>())
            collider.enabled = false;
        GetComponent<ProjectileComicEffects>()?.EndFlight();
    }
}
