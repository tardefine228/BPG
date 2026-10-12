using UnityEngine;

// Взрывная волна вокруг кота. Наносит урон мышам, повреждает блоки и отбрасывает тела.
public sealed class BlastCatAbility : CatAbility
{
    [SerializeField, Min(0.1f)] private float radius = 4f;
    [SerializeField, Min(0f)] private float enemyDamage = 60f;
    [SerializeField, Min(0f)] private float blockHitStrength = 10f;
    [SerializeField, Min(0f)] private float explosionImpulse = 14f;
    [SerializeField] private LayerMask affectedLayers = ~0;

    [Header("Визуал взрыва")]
    [SerializeField] private Color flashColor = new Color(1f, 0.85f, 0.35f, 0.9f);
    [SerializeField, Min(0.05f)] private float flashDuration = 0.25f;

    protected override void Activate()
    {
        BlastUtility.Explode(Body.position, radius, enemyDamage,
            blockHitStrength, explosionImpulse, affectedLayers, Body);
        SpawnVisuals();
    }

    // Короткая вспышка + расходящееся кольцо, чтобы игрок видел срабатывание.
    private void SpawnVisuals()
    {
        GameObject flashGo = new GameObject("BlastFlash");
        flashGo.transform.SetParent(transform.parent, true);
        flashGo.transform.position = Body.position;
        SpriteRenderer flash = flashGo.AddComponent<SpriteRenderer>();
        flash.sprite = CreateCircleSprite(Mathf.CeilToInt(radius * 64));
        flash.color = flashColor;
        flash.sortingOrder = 20000;
        Destroy(flashGo, flashDuration);

        StartCoroutine(ExpandRing(flashGo, flash));
    }

    private System.Collections.IEnumerator ExpandRing(GameObject ring, SpriteRenderer sr)
    {
        Vector3 start = new Vector3(0.3f, 0.3f, 1f);
        Vector3 end = new Vector3(radius * 2f, radius * 2f, 1f);
        Color c = sr.color;
        float elapsed = 0f;
        while (elapsed < flashDuration && ring != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / flashDuration);
            ring.transform.localScale = Vector3.Lerp(start, end, t);
            c.a = Mathf.Lerp(0.9f, 0f, t);
            sr.color = c;
            yield return null;
        }
        if (ring != null)
            Destroy(ring);
    }

    private static Sprite CreateCircleSprite(int resolution)
    {
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        float center = resolution * 0.5f;
        for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float dx = x - center + 0.5f, dy = y - center + 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / center;
                float a = Mathf.Clamp01((1f - d) * resolution * 0.12f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        Sprite sprite = Sprite.Create(tex,
            new Rect(0, 0, resolution, resolution),
            new Vector2(0.5f, 0.5f), resolution);
        return sprite;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
