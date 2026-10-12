using System.Collections.Generic;
using UnityEngine;

// Готовый эффект из сцены повторно используется после затухания частиц.
public sealed class ComicParticleBurst : MonoBehaviour
{
    public enum AbilityEffect { Split, Speed, Blast, Inflate }
    [SerializeField] private ParticleSystem dust;
    [SerializeField] private ParticleSystem chips;
    [SerializeField] private ParticleSystem flash;
    public bool IsPlaying => pulseRemaining > 0f || dust.IsAlive() || chips.IsAlive() || flash.IsAlive();

    // Один небольшой меш на слот пула: геометрия и буферы переиспользуются,
    // а короткие вспышки остаются читаемыми без bloom/post-processing.
    private readonly List<Vector3> vertices = new List<Vector3>(4096);
    private readonly List<Color> colors = new List<Color>(4096);
    private readonly List<Vector2> uvs = new List<Vector2>(4096);
    private readonly List<int> triangles = new List<int>(6144);
    private Mesh pulseMesh;
    private MeshRenderer pulseRenderer;
    private Transform pulseRoot;
    private Transform followTarget;
    private Vector3 followOffset;
    private Vector2 pulseDirection;
    private Color pulseColor;
    private AbilityEffect pulseEffect;
    private float pulseScale;
    private float pulseRadius;
    private float pulseDuration;
    private float pulseRemaining;
    private float pulseIntensity;
    private float pulseWidth;
    private static readonly Vector2[] BoltPoints =
    {
        new Vector2(-1.05f, 0.16f), new Vector2(-0.3f, -0.08f),
        new Vector2(-0.43f, 0.27f), new Vector2(0.45f, -0.12f)
    };

    public static void Play(Material source, Vector3 position, Vector2 normal,
        bool boxHit, Color dustColor, Color chipColor, Color flashColor,
        int dustCount, int chipCount, float scale, int sortingLayer, int sortingOrder)
    {
        if (source == null || ComicEffectsPool.Instance == null) return;
        ComicEffectsPool.Instance.Next().Begin(position, normal, boxHit, dustColor,
            chipColor, flashColor, dustCount, chipCount, scale, sortingLayer, sortingOrder);
    }

    // Невидимые частицы заранее подготавливают материал и буферы рендера.
    public void WarmUp(Vector3 position)
    {
        EnsurePulseGeometry(Resources.Load<Material>("Materials/M_ComicFX"));
        Begin(position, Vector2.up, true, Color.clear, Color.clear, Color.clear,
            9, 7, 1.5f, 0, 10);
    }

    public void Begin(Vector3 origin, Vector2 normal, bool boxHit, Color dustColor,
        Color chipColor, Color flashColor, int dustCount, int chipCount, float scale,
        int sortingLayer, int sortingOrder)
    {
        StopPulse();
        if (normal.sqrMagnitude < 0.001f) normal = Vector2.up;
        normal.Normalize();
        scale = Mathf.Max(0.05f, scale);
        Emit(dust, 0, origin, normal, dustColor, dustCount, scale, sortingLayer, sortingOrder);
        Emit(chips, 1, origin, normal, chipColor, boxHit ? chipCount : 0, scale, sortingLayer, sortingOrder + 1);
        Emit(flash, 2, origin, normal, flashColor, boxHit ? 4 : 2, scale, sortingLayer, sortingOrder + 2);
    }

    // Общий пул хранит частицы в мировых координатах: эффект переживает
    // удаление оригинала при разделении и останавливается вместе с паузой.
    public void BeginAbility(Vector3 origin, Vector2 direction, AbilityEffect effect,
        Color color, float scale, float radius, int sortingLayer, int sortingOrder,
        Material material = null, float duration = 0.72f, float intensity = 1f,
        float strokeWidth = 0.1f, Transform follow = null)
    {
        StopPulse();
        PrepareAbilityParticles(dust, sortingLayer, sortingOrder);
        PrepareAbilityParticles(chips, sortingLayer, sortingOrder + 1);
        PrepareAbilityParticles(flash, sortingLayer, sortingOrder + 2);
        if (intensity <= 0f) return;
        direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
        scale = Mathf.Max(0.05f, scale);
        duration = Mathf.Max(0.2f, duration);
        intensity = Mathf.Clamp(intensity, 0f, 1.5f);
        EnsurePulseGeometry(material);
        pulseEffect = effect;
        pulseColor = color;
        pulseDirection = direction;
        pulseScale = scale;
        pulseRadius = Mathf.Max(radius, scale * 2.2f);
        pulseDuration = pulseRemaining = duration;
        pulseIntensity = intensity;
        pulseWidth = scale * Mathf.Clamp(strokeWidth, 0.03f, 0.3f);
        followTarget = follow;
        followOffset = follow != null ? origin - follow.position : Vector3.zero;
        pulseRoot.position = origin;
        pulseRenderer.sortingLayerID = sortingLayer;
        pulseRenderer.sortingOrder = sortingOrder + 3;
        pulseRenderer.enabled = true;
        DrawPulse(0f);

        dust.Play();
        flash.Play();
        bool blast = effect == AbilityEffect.Blast;
        bool speed = effect == AbilityEffect.Speed;
        bool split = effect == AbilityEffect.Split;
        bool inflate = effect == AbilityEffect.Inflate;
        int count = blast ? 64 : speed ? 12 : split ? 14 : 42;
        float lifetime = duration * (blast ? 0.85f : 0.8f);
        Vector2 tangent = new Vector2(-direction.y, direction.x);
        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.PI * 2f / count + Random.Range(-0.08f, 0.08f);
            Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 offset = speed ? (-direction * Random.Range(0.45f, 0.95f)
                    + tangent * Random.Range(-0.65f, 0.65f)) * scale
                : radial * scale * (inflate ? 0.9f : blast ? 0.2f : 0.6f);
            Vector2 velocity = speed ? (-direction * Random.Range(1.4f, 3.2f)
                    + tangent * Random.Range(-0.25f, 0.25f)) * scale
                : radial * (blast ? pulseRadius / lifetime : scale * (inflate ? 2f : split ? 2.2f : 3.8f));
            Color spark = i % 3 == 0 ? new Color(1f, 0.98f, 0.88f, 1f) : color;
            if (blast && i % 4 == 0) spark = new Color(1f, 0.8f, 0.16f, 1f);
            spark.a *= Mathf.Clamp01(intensity) * (split ? 0.65f : 1f);
            flash.Emit(new ParticleSystem.EmitParams
            {
                position = origin + (Vector3)offset,
                velocity = velocity,
                startColor = spark,
                startSize3D = new Vector3(scale * (speed ? 0.055f : split ? 0.14f : 0.3f),
                    scale * (speed ? Random.Range(0.18f, 0.36f) : split ? 0.24f : 0.5f), scale * 0.3f),
                startLifetime = lifetime * Random.Range(0.65f, 1.05f),
                rotation = angle * Mathf.Rad2Deg
            }, 1);
            if (speed || split || i % 2 != 0) continue;
            Color smoke = blast ? new Color(0.27f, 0.22f, 0.3f, 0.7f) : color;
            smoke.a = (blast ? 0.7f : inflate ? 0.52f : 0.3f) * Mathf.Clamp01(intensity);
            dust.Emit(new ParticleSystem.EmitParams
            {
                position = origin + (Vector3)offset,
                velocity = velocity * (blast ? 0.32f : 0.4f)
                    + (blast ? Vector2.up * scale * 0.65f : Vector2.zero),
                startColor = smoke,
                startSize = scale * Random.Range(blast ? 0.65f : 0.35f, blast ? 1.2f : 0.65f),
                startLifetime = duration * (blast ? 1.65f : inflate ? 1.3f : 0.9f),
                rotation = Random.Range(0f, 360f)
            }, 1);
        }
    }

    private static void PrepareAbilityParticles(ParticleSystem particles, int layer, int order)
    {
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = false;
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sortingLayerID = layer;
        renderer.sortingOrder = order;
    }

    private void EnsurePulseGeometry(Material material)
    {
        if (pulseRenderer == null)
        {
            GameObject root = new GameObject("AbilityPulse");
            pulseRoot = root.transform;
            pulseRoot.SetParent(transform, false);
            pulseMesh = new Mesh { name = "Pooled ability pulse" };
            pulseMesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh = pulseMesh;
            pulseRenderer = root.AddComponent<MeshRenderer>();
            pulseRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pulseRenderer.receiveShadows = false;
            pulseRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            pulseRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            pulseRenderer.enabled = false;
        }
        if (material != null) pulseRenderer.sharedMaterial = material;
    }

    private void LateUpdate()
    {
        if (pulseRemaining <= 0f || Time.deltaTime <= 0f) return;
        pulseRemaining = Mathf.Max(0f, pulseRemaining - Time.deltaTime);
        if (pulseRemaining <= 0f)
        {
            StopPulse();
            return;
        }
        if (followTarget != null) pulseRoot.position = followTarget.position + followOffset;
        DrawPulse(1f - pulseRemaining / pulseDuration);
    }

    private void StopPulse()
    {
        pulseRemaining = 0f;
        followTarget = null;
        if (pulseRenderer != null) pulseRenderer.enabled = false;
    }

    private void OnDisable() => StopPulse();

    private void OnDestroy()
    {
        if (pulseMesh != null) Destroy(pulseMesh);
    }

    private void DrawPulse(float t)
    {
        vertices.Clear();
        colors.Clear();
        uvs.Clear();
        triangles.Clear();
        float fade = 1f - Mathf.SmoothStep(0.25f, 1f, t);
        Color color = Tint(pulseColor, fade);
        Color white = Tint(new Color(1f, 0.98f, 0.88f, 1f), fade);
        float s = pulseScale;
        float width = pulseWidth;
        float expansion = 1f - Mathf.Pow(1f - t, 3f);
        float flashFade = 1f - Mathf.SmoothStep(0.06f, 0.45f, t);
        if (pulseEffect != AbilityEffect.Speed && pulseEffect != AbilityEffect.Split)
            AddHalo(Vector2.zero, s * Mathf.Lerp(1.3f, 2.2f, expansion),
                Tint(pulseColor, flashFade * 0.24f));

        switch (pulseEffect)
        {
            case AbilityEffect.Speed:
                // Тонкий короткий блик над котом и несколько штрихов позади:
                // движение читается без больших колец, ореола и звёздной вспышки.
                AddBolt(s, width * 0.7f, Tint(pulseColor, flashFade),
                    Tint(new Color(1f, 0.98f, 0.88f, 1f), flashFade * 0.75f));
                for (int i = 0; i < 7; i++)
                {
                    float side = (i - 3f) * s * 0.24f;
                    float end = s * (-0.55f - t * 0.35f - (i % 3) * 0.16f);
                    Vector2 tip = Along(end, side);
                    Vector2 tail = Along(end - s * (0.65f + (i % 3) * 0.2f), side);
                    AddStroke(tail, tip, width * (i % 2 == 0 ? 0.65f : 0.4f),
                        Tint(pulseColor, fade * 0.05f), Tint(pulseColor, fade * 0.8f));
                    AddStroke(Vector2.Lerp(tail, tip, 0.35f), tip, width * 0.28f,
                        Tint(Color.white, fade * 0.03f), Tint(Color.white, fade * 0.55f));
                }
                break;
            case AbilityEffect.Split:
                AddRing(Vector2.zero, s * Mathf.Lerp(0.6f, 1.65f, expansion), width * 0.7f,
                    Tint(pulseColor, fade * 0.65f), true);
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * Mathf.PI / 3f + 0.2f;
                    Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Vector2 center = radial * s * Mathf.Lerp(0.9f, 1.9f, expansion);
                    AddStroke(center - radial * s * 0.25f, center,
                        width * 0.35f, Tint(pulseColor, fade * 0.05f), Tint(pulseColor, fade * 0.6f));
                }
                break;
            case AbilityEffect.Blast:
                float wave = Mathf.Lerp(s * 0.55f, pulseRadius, expansion);
                AddRing(Vector2.zero, wave, width * 1.6f, color, false);
                AddRing(Vector2.zero, wave * 0.82f, width * 0.55f,
                    Tint(new Color(1f, 0.85f, 0.3f, 1f), fade * 0.85f), true);
                AddStar(Vector2.zero, s * Mathf.Lerp(1.5f, 2.15f, expansion), s * 0.55f, 12,
                    Tint(pulseColor, flashFade * 0.85f), 0f);
                AddStar(Vector2.zero, s * 1.1f, s * 0.38f, 8,
                    Tint(new Color(1f, 0.93f, 0.6f, 1f), flashFade), 0.12f);
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI / 8f;
                    Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    AddStroke(radial * wave * 0.8f, radial * (wave + s * 0.55f),
                        width * (i % 2 == 0 ? 0.85f : 0.5f), color, Tint(pulseColor, 0f));
                }
                break;
            case AbilityEffect.Inflate:
                float wobble = Mathf.Sin(t * Mathf.PI * 4f) * 0.09f * (1f - t);
                float growth = s * (Mathf.Lerp(0.75f, 1.8f, expansion) + wobble);
                AddRing(Vector2.zero, growth, width * 1.2f, color, false);
                AddRing(Vector2.zero, growth * 1.24f, width * 0.5f,
                    Tint(pulseColor, fade * 0.6f), true);
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * Mathf.PI / 6f + t * 0.18f;
                    Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    AddStroke(radial * growth * 0.83f, radial * growth * 1.3f,
                        width * 0.65f, white, Tint(pulseColor, fade * 0.1f));
                }
                AddStar(Vector2.zero, s * 0.85f, s * 0.2f, 8,
                    Tint(Color.white, flashFade * 0.6f), 0.1f);
                break;
        }
        pulseMesh.Clear();
        pulseMesh.SetVertices(vertices);
        pulseMesh.SetColors(colors);
        pulseMesh.SetUVs(0, uvs);
        pulseMesh.SetTriangles(triangles, 0, true);
    }

    private Color Tint(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(color.a * alpha * pulseIntensity);
        return color;
    }

    private Vector2 Along(float forward, float side) => pulseDirection * forward
        + new Vector2(-pulseDirection.y, pulseDirection.x) * side;

    private void AddBolt(float scale, float width, Color color, Color core)
    {
        // Небольшой золотой зигзаг на верхнем крае спрайта.
        for (int i = 0; i < BoltPoints.Length - 1; i++)
        {
            Vector2 a = Along(BoltPoints[i].x * scale, (BoltPoints[i].y + 0.85f) * scale);
            Vector2 b = Along(BoltPoints[i + 1].x * scale, (BoltPoints[i + 1].y + 0.85f) * scale);
            AddStroke(a, b, width, color, color);
            AddStroke(a, b, width * 0.3f, core, core);
        }
    }

    private void AddRing(Vector2 center, float radius, float width, Color color, bool broken)
    {
        const int segments = 48;
        Color edge = color;
        edge.a = 0f;
        for (int i = 0; i < segments; i++)
        {
            if (broken && i % 8 >= 6) continue;
            float a = i * Mathf.PI * 2f / segments;
            float b = (i + 1) * Mathf.PI * 2f / segments;
            Vector2 da = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 db = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
            AddQuad(center + da * (radius - width), center + db * (radius - width),
                center + db * (radius - width * 0.35f), center + da * (radius - width * 0.35f),
                edge, edge, color, color);
            AddQuad(center + da * (radius - width * 0.35f), center + db * (radius - width * 0.35f),
                center + db * (radius + width * 0.35f), center + da * (radius + width * 0.35f),
                color, color, color, color);
            AddQuad(center + da * (radius + width * 0.35f), center + db * (radius + width * 0.35f),
                center + db * (radius + width), center + da * (radius + width),
                color, color, edge, edge);
        }
    }

    private void AddHalo(Vector2 center, float radius, Color color)
    {
        Color edge = color;
        edge.a = 0f;
        const int segments = 40;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            float b = (i + 1) * Mathf.PI * 2f / segments;
            AddTriangle(center, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, color, edge, edge);
        }
    }

    private void AddStar(Vector2 center, float outer, float inner, int points, Color color, float rotation)
    {
        for (int i = 0; i < points * 2; i++)
        {
            float a = i * Mathf.PI / points + rotation;
            float b = (i + 1) * Mathf.PI / points + rotation;
            AddTriangle(center, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? outer : inner),
                center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * (i % 2 == 0 ? inner : outer),
                color, color, color);
        }
    }

    private void AddStroke(Vector2 start, Vector2 end, float width, Color from, Color to)
    {
        Vector2 direction = end - start;
        if (direction.sqrMagnitude < 0.0001f) return;
        Vector2 side = new Vector2(-direction.y, direction.x).normalized * width * 0.5f;
        AddQuad(start - side, start + side, end + side * 0.5f, end - side * 0.5f, from, from, to, to);
    }

    private void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
    {
        int index = vertices.Count;
        AddVertex(a, ca); AddVertex(b, cb); AddVertex(c, cc); AddVertex(d, cd);
        triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
        triangles.Add(index); triangles.Add(index + 2); triangles.Add(index + 3);
    }

    private void AddTriangle(Vector2 a, Vector2 b, Vector2 c, Color ca, Color cb, Color cc)
    {
        int index = vertices.Count;
        AddVertex(a, ca); AddVertex(b, cb); AddVertex(c, cc);
        triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
    }

    private void AddVertex(Vector2 point, Color color)
    {
        vertices.Add(new Vector3(point.x, point.y, 0f));
        colors.Add(color);
        uvs.Add(new Vector2(0.5f, 0.5f));
    }

    private static void Emit(ParticleSystem particles, int shape, Vector3 origin,
        Vector2 normal, Color color, int count, float scale, int sortingLayer, int sortingOrder)
    {
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sortingLayerID = sortingLayer;
        renderer.sortingOrder = sortingOrder;
        count = Mathf.Clamp(count, 0, 100);
        if (count == 0) return;
        particles.Play();
        Vector2 tangent = new Vector2(-normal.y, normal.x);
        for (int i = 0; i < count; i++)
        {
            Vector2 velocity = normal * Random.Range(1.4f, 4f) + tangent * Random.Range(-3.8f, 3.8f);
            float tint = Random.Range(0.85f, 1.1f);
            var particle = new ParticleSystem.EmitParams
            {
                position = origin + (Vector3)(Random.insideUnitCircle * (0.12f * scale)),
                velocity = (Vector3)(velocity * scale * (shape == 0 ? 0.65f : 1f)),
                startColor = new Color(color.r * tint, color.g * tint, color.b * tint, color.a),
                startSize = scale * (shape == 0 ? Random.Range(0.45f, 0.9f)
                    : shape == 1 ? Random.Range(0.18f, 0.45f) : Random.Range(0.35f, 0.7f)),
                startLifetime = shape == 2 ? Random.Range(0.18f, 0.3f) : Random.Range(0.45f, 1.1f),
                rotation = Random.Range(0f, 360f)
            };
            particles.Emit(particle, 1);
        }
    }
}
