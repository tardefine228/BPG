using UnityEngine;

// Готовый эффект из сцены повторно используется после затухания частиц.
public sealed class ComicParticleBurst : MonoBehaviour
{
    [SerializeField] private ParticleSystem dust;
    [SerializeField] private ParticleSystem chips;
    [SerializeField] private ParticleSystem flash;
    public bool IsPlaying => dust.IsAlive() || chips.IsAlive() || flash.IsAlive();

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
        Begin(position, Vector2.up, true, Color.clear, Color.clear, Color.clear,
            9, 7, 1.5f, 0, 10);
    }

    public void Begin(Vector3 origin, Vector2 normal, bool boxHit, Color dustColor,
        Color chipColor, Color flashColor, int dustCount, int chipCount, float scale,
        int sortingLayer, int sortingOrder)
    {
        if (normal.sqrMagnitude < 0.001f) normal = Vector2.up;
        normal.Normalize();
        scale = Mathf.Max(0.05f, scale);
        Emit(dust, 0, origin, normal, dustColor, dustCount, scale, sortingLayer, sortingOrder);
        Emit(chips, 1, origin, normal, chipColor, boxHit ? chipCount : 0, scale, sortingLayer, sortingOrder + 1);
        Emit(flash, 2, origin, normal, flashColor, boxHit ? 4 : 2, scale, sortingLayer, sortingOrder + 2);
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
