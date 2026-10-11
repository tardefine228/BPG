using System.Collections.Generic;
using UnityEngine;

// Creates a short burst in the XY plane. It lives independently of the projectile.
public sealed class ComicParticleBurst : MonoBehaviour
{
    private readonly List<Material> materials = new List<Material>();
    private readonly List<Texture2D> textures = new List<Texture2D>();
    private float remainingLifetime;

    public static void Play(Material source, Vector3 position, Vector2 normal,
        bool boxHit, Color dustColor, Color chipColor, Color flashColor,
        int dustCount, int chipCount, float scale, int sortingLayer, int sortingOrder)
    {
        if (source == null)
            return;

        GameObject root = new GameObject(boxHit ? "Comic_BoxImpact" : "Comic_GroundImpact");
        root.transform.position = position;
        ComicParticleBurst burst = root.AddComponent<ComicParticleBurst>();
        burst.remainingLifetime = 1.8f;
        scale = Mathf.Max(0.05f, scale);
        if (normal.sqrMagnitude < 0.001f)
            normal = Vector2.up;
        normal.Normalize();

        burst.Emit(source, "Dust", 0, position, normal, dustColor,
            Mathf.Clamp(dustCount, 0, 100), scale, false, sortingLayer, sortingOrder);
        if (boxHit)
        {
            burst.Emit(source, "Chips", 1, position, normal, chipColor,
                Mathf.Clamp(chipCount, 0, 100), scale, true, sortingLayer, sortingOrder + 1);
        }
        burst.Emit(source, "Flash", 2, position, normal, flashColor,
            boxHit ? 4 : 2, scale, false, sortingLayer, sortingOrder + 2);
    }

    private void Emit(Material source, string label, int shape, Vector3 origin,
        Vector2 normal, Color color, int count, float scale, bool falling,
        int sortingLayer, int sortingOrder)
    {
        if (count == 0)
            return;

        GameObject obj = new GameObject(label);
        obj.transform.SetParent(transform, false);
        ParticleSystem particles = obj.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.1f;
        main.startLifetime = shape == 2 ? 0.3f : 0.85f;
        main.startSpeed = 0f;
        main.gravityModifier = falling ? 0.9f : -0.05f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 110;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = particles.emission;
        emission.enabled = false;
        var shapeModule = particles.shape;
        shapeModule.enabled = false;

        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, shape == 0
            ? new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.3f))
            : AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
        var fade = particles.colorOverLifetime;
        fade.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.3f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var rotation = particles.rotationOverLifetime;
        rotation.enabled = shape == 1;
        rotation.z = new ParticleSystem.MinMaxCurve(-7f, 7f);

        Texture2D texture = CreateTexture(shape);
        Material material = new Material(source) { name = "Comic_" + label };
        material.mainTexture = texture;
        material.color = Color.white;
        textures.Add(texture);
        materials.Add(material);
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingLayerID = sortingLayer;
        renderer.sortingOrder = sortingOrder;

        particles.Play();
        Vector2 tangent = new Vector2(-normal.y, normal.x);
        for (int i = 0; i < count; i++)
        {
            Vector2 velocity = normal * Random.Range(1.4f, 4f)
                + tangent * Random.Range(-3.8f, 3.8f);
            float tint = Random.Range(0.85f, 1.1f);
            Color particleColor = new Color(color.r * tint, color.g * tint, color.b * tint, color.a);
            var particle = new ParticleSystem.EmitParams
            {
                position = origin + (Vector3)(Random.insideUnitCircle * (0.12f * scale)),
                velocity = (Vector3)(velocity * scale * (shape == 0 ? 0.65f : 1f)),
                startColor = particleColor,
                startSize = scale * (shape == 0 ? Random.Range(0.45f, 0.9f)
                    : shape == 1 ? Random.Range(0.18f, 0.45f) : Random.Range(0.35f, 0.7f)),
                startLifetime = shape == 2 ? Random.Range(0.18f, 0.3f) : Random.Range(0.45f, 1.1f),
                rotation = Random.Range(0f, 360f)
            };
            particles.Emit(particle, 1);
        }
    }

    // Filled comic shapes with an ink border; no imported image is required.
    private static Texture2D CreateTexture(int shape)
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "ComicParticleShape",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float px = (x + 0.5f - size * 0.5f) / (size * 0.46f);
            float py = (y + 0.5f - size * 0.5f) / (size * 0.46f);
            float distance = shape == 0 ? Mathf.Sqrt(px * px + py * py)
                : shape == 1 ? Mathf.Abs(px) * 1.4f + Mathf.Abs(py) * 0.75f
                : Mathf.Pow(Mathf.Abs(px), 0.55f) + Mathf.Pow(Mathf.Abs(py), 0.55f);
            pixels[y * size + x] = distance > 1f ? new Color32(0, 0, 0, 0)
                : distance > 0.78f ? new Color32(25, 23, 31, 255)
                : new Color32(255, 255, 255, 255);
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void Update()
    {
        remainingLifetime -= Time.deltaTime;
        if (remainingLifetime <= 0f)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        foreach (Material material in materials)
            if (material != null) Destroy(material);
        foreach (Texture2D texture in textures)
            if (texture != null) Destroy(texture);
    }
}
