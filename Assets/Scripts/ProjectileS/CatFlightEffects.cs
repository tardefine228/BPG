using UnityEngine;

// Шерсть в полёте и декоративный отскок после первого физического попадания.
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

    [Header("Отскок на передний план")]
    [SerializeField, Min(0f)] private float hopSpeed = 12f;
    [SerializeField, Min(0f)] private float sidewaysSpeed = 4f;
    [SerializeField, Min(1f)] private float fallGravity = 45f;
    [SerializeField, Min(1f)] private float foregroundScale = 1.35f;
    [SerializeField, Min(0.01f)] private float approachDuration = 0.18f;
    [SerializeField] private float spinSpeed = 180f;
    [SerializeField] private int foregroundSortingOrder = 30000;
    [SerializeField, Min(0f)] private float cleanupMargin = 4f;
    [SerializeField, Min(0.5f)] private float maxExitDuration = 4f;

    public bool IsExiting { get; private set; }
    private Rigidbody2D body;
    private SpriteRenderer sprite;
    private ParticleSystem fur;
    private Camera view;
    private bool flying;
    private bool exitRequested;
    private Vector2 flightVelocity;
    private Vector2 exitVelocity;
    private Vector3 originalScale;
    private Vector3 previousFurPosition;
    private float particlesToEmit;
    private float exitTime;
    private float fieldBottom;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        sprite = GetComponent<SpriteRenderer>();
    }

    public void BeginFlight(float mapBottom)
    {
        flying = true;
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
        if (flying && !exitRequested)
            flightVelocity = body.linearVelocity;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!flying || exitRequested || IsExiting)
            return;
        exitRequested = true;
        if (fur != null)
            fur.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        // Физику отключаем после доставки всех обработчиков столкновения:
        // ящик успевает получить удар, а камера — точку попадания.
    }

    private void LateUpdate()
    {
        if (Time.timeScale <= 0f)
            return;
        if (exitRequested && !IsExiting)
            BeginExit();
        if (IsExiting)
        {
            float dt = Time.deltaTime;
            exitTime += dt;
            transform.position += (Vector3)(exitVelocity * dt + Vector2.down * (0.5f * fallGravity * dt * dt));
            exitVelocity.y -= fallGravity * dt;
            transform.Rotate(0f, 0f, -Mathf.Sign(exitVelocity.x) * spinSpeed * dt);
            float approach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(exitTime / approachDuration));
            transform.localScale = originalScale * Mathf.Lerp(1f, foregroundScale, approach);
            float bottom = fieldBottom - cleanupMargin;
            if (view != null && view.orthographic)
                bottom = Mathf.Min(bottom, view.transform.position.y - view.orthographicSize - cleanupMargin);
            if ((exitVelocity.y < 0f && transform.position.y < bottom) || exitTime >= maxExitDuration)
                Destroy(gameObject);
            return;
        }
        if (!flying || fur == null)
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
        originalScale = transform.localScale;
        float direction = flightVelocity.x < 0f ? -1f : 1f;
        exitVelocity = new Vector2(direction * sidewaysSpeed, hopSpeed);
        body.simulated = false;
        foreach (var collider in GetComponentsInChildren<Collider2D>())
            collider.enabled = false;
        GetComponent<ProjectileComicEffects>()?.EndFlight();
        var friction = GetComponent<ProjectileSurfaceFriction>();
        if (friction != null)
            friction.enabled = false;
        sprite.sortingOrder = foregroundSortingOrder;
    }
}
