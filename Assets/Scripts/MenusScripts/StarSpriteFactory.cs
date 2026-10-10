using UnityEngine;

/// <summary>
/// Builds gold / empty star sprites at runtime so the lobby does not depend on extra atlas assets.
/// </summary>
public static class StarSpriteFactory
{
    private static readonly Color32 Gold = new Color32(230, 155, 82, 255);
    private static readonly Color32 GoldEdge = new Color32(186, 110, 42, 255);
    private static readonly Color32 EmptyFill = new Color32(70, 70, 70, 255);
    private static readonly Color32 EmptyEdge = new Color32(150, 150, 150, 255);

    public static Sprite Create(bool filled, int size = 96)
    {
        size = Mathf.Max(32, size);
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = filled ? "StarOn" : "StarOff"
        };

        Color32[] pixels = new Color32[size * size];
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float outer = size * 0.46f;
        float inner = outer * 0.42f;
        Vector2[] polygon = BuildStar(center, outer, inner);
        Vector2[] innerPolygon = filled ? null : BuildStar(center, outer * 0.72f, inner * 0.72f);

        Color32 fill = filled ? Gold : EmptyFill;
        Color32 edge = filled ? GoldEdge : EmptyEdge;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 point = new Vector2(x, y);
                bool inside = Contains(polygon, point);
                bool hole = innerPolygon != null && Contains(innerPolygon, point);
                if (inside && !hole)
                {
                    float edgeDistance = EdgeDistance(polygon, point);
                    pixels[y * size + x] = edgeDistance < 1.6f ? edge : fill;
                }
                else
                {
                    pixels[y * size + x] = new Color32(0, 0, 0, 0);
                }
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size);
    }

    private static Vector2[] BuildStar(Vector2 center, float outer, float inner)
    {
        Vector2[] points = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float angle = -Mathf.PI * 0.5f + i * Mathf.PI * 0.2f;
            float radius = (i % 2 == 0) ? outer : inner;
            points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        return points;
    }

    private static bool Contains(Vector2[] polygon, Vector2 point)
    {
        int winding = 0;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            Vector2 a = polygon[j];
            Vector2 b = polygon[i];
            if (a.y <= point.y)
            {
                if (b.y > point.y && IsLeft(a, b, point) > 0f)
                {
                    winding++;
                }
            }
            else if (b.y <= point.y && IsLeft(a, b, point) < 0f)
            {
                winding--;
            }
        }

        return winding != 0;
    }

    private static float IsLeft(Vector2 a, Vector2 b, Vector2 point)
    {
        return (b.x - a.x) * (point.y - a.y) - (point.x - a.x) * (b.y - a.y);
    }

    private static float EdgeDistance(Vector2[] polygon, Vector2 point)
    {
        float min = float.MaxValue;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Length];
            min = Mathf.Min(min, DistanceToSegment(point, a, b));
        }

        return min;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float length = ab.sqrMagnitude;
        if (length < 0.0001f)
        {
            return Vector2.Distance(point, a);
        }

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / length);
        return Vector2.Distance(point, a + ab * t);
    }
}
