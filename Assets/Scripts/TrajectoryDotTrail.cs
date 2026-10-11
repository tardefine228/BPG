using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws the aim preview of the slingshot as a row of dots (the classic
/// "dotted trail"): the dots follow the predicted flight path, are evenly
/// spaced along it, keep their screen size while the camera zooms and fade out
/// towards the end of the visible part of the path.
/// </summary>
/// <remarks>
/// The trail builds its own sprite in code, so it does not need any art asset.
/// One instance pools a fixed number of dots; <see cref="Show"/> repositions
/// them, <see cref="Hide"/> switches them off. Always call <see cref="Dispose"/>
/// when the owner is destroyed.
/// </remarks>
public sealed class TrajectoryDotTrail
{
    private const int DotTextureSize = 96;
    private const int Supersampling = 3;

    private readonly GameObject container;
    private readonly Texture2D dotTexture;
    private readonly Sprite dotSprite;
    private readonly SpriteRenderer[] dots;
    private readonly Color baseColor;
    private int visibleCount;

    /// <summary>Dots below this distance from the start are never shown.</summary>
    private const float MinimumPathLength = 0.05f;

    public TrajectoryDotTrail(Transform parent, int capacity, int sortingOrder,
        Color fillColor, Color outlineColor)
    {
        capacity = Mathf.Max(2, capacity);
        baseColor = fillColor;

        container = new GameObject("TrajectoryDots");
        container.transform.SetParent(parent, false);
        // The trail works in world coordinates, so cancel out whatever
        // rotation or scale the owner carries.
        Vector3 parentScale = parent.lossyScale;
        container.transform.localRotation = Quaternion.Inverse(parent.rotation);
        container.transform.localScale = new Vector3(
            1f / Mathf.Max(0.0001f, parentScale.x),
            1f / Mathf.Max(0.0001f, parentScale.y),
            1f);
        container.transform.position = Vector3.zero;
        container.layer = parent.gameObject.layer;

        dotTexture = BuildDotTexture(fillColor, outlineColor, out float contentFraction);
        dotSprite = Sprite.Create(
            dotTexture,
            new Rect(0f, 0f, dotTexture.width, dotTexture.height),
            new Vector2(0.5f, 0.5f),
            dotTexture.width / Mathf.Max(0.05f, contentFraction));
        dotSprite.name = "TrajectoryDot";

        dots = new SpriteRenderer[capacity];
        for (int i = 0; i < capacity; i++)
        {
            GameObject dot = new GameObject("Dot" + i);
            dot.layer = parent.gameObject.layer;
            dot.transform.SetParent(container.transform, false);

            SpriteRenderer renderer = dot.AddComponent<SpriteRenderer>();
            renderer.sprite = dotSprite;
            renderer.sortingOrder = sortingOrder;
            renderer.color = baseColor;
            dot.SetActive(false);
            dots[i] = renderer;
        }
    }

    /// <summary>
    /// Places the dots along <paramref name="path"/>, showing only the first
    /// <paramref name="visibleFraction"/> of its length with a fade towards the
    /// far end. Sizes are given in world units.
    /// </summary>
    public void Show(IList<Vector2> path, float visibleFraction, float dotSize,
        float dotGap, float endScale)
    {
        if (path == null || path.Count < 2)
        {
            Hide();
            return;
        }

        float totalLength = PathLength(path);
        if (totalLength <= MinimumPathLength)
        {
            Hide();
            return;
        }

        float visibleLength = Mathf.Clamp(visibleFraction, 0.05f, 1f) * totalLength;
        float gap = Mathf.Max(0.02f, dotGap);

        // Evenly spaced samples: the dot count follows the visible length, but
        // stays inside the pool and never collapses into a single blob.
        int count = Mathf.Clamp(
            Mathf.FloorToInt(visibleLength / gap) + 1, 2, dots.Length);

        float size = Mathf.Max(0.01f, dotSize);
        for (int i = 0; i < count; i++)
        {
            // Every dot sits in the middle of its own slice of the visible
            // path, so the first dot never covers the shell in the pouch.
            float t = (i + 0.5f) / count;
            Vector2 point = SamplePath(path, visibleLength * t);

            SpriteRenderer dot = dots[i];
            dot.transform.localPosition = new Vector3(point.x, point.y, 0f);

            float scale = size * Mathf.Lerp(1f, Mathf.Clamp(endScale, 0.2f, 1f), t);
            dot.transform.localScale = new Vector3(scale, scale, 1f);

            // Bright at the start of the trail, fading out towards its end.
            float alpha = Mathf.Lerp(1f, 0.10f, Mathf.Pow(t, 1.25f));
            dot.color = new Color(baseColor.r, baseColor.g, baseColor.b,
                baseColor.a * alpha);

            if (i >= visibleCount)
            {
                dot.gameObject.SetActive(true);
            }
        }

        for (int i = count; i < visibleCount; i++)
        {
            dots[i].gameObject.SetActive(false);
        }

        visibleCount = count;
    }

    /// <summary>Hides the whole trail.</summary>
    public void Hide()
    {
        for (int i = 0; i < visibleCount; i++)
        {
            if (dots[i] != null)
            {
                dots[i].gameObject.SetActive(false);
            }
        }

        visibleCount = 0;
    }

    /// <summary>Destroys the pooled dots, the shared sprite and its texture.</summary>
    public void Dispose()
    {
        if (container != null)
        {
            Object.Destroy(container);
        }

        if (dotSprite != null)
        {
            Object.Destroy(dotSprite);
        }

        if (dotTexture != null)
        {
            Object.Destroy(dotTexture);
        }
    }

    private static float PathLength(IList<Vector2> path)
    {
        float length = 0f;
        for (int i = 1; i < path.Count; i++)
        {
            length += Vector2.Distance(path[i - 1], path[i]);
        }

        return length;
    }

    /// <summary>Point on the polyline at the given distance from its start.</summary>
    private static Vector2 SamplePath(IList<Vector2> path, float distance)
    {
        float travelled = 0f;
        for (int i = 1; i < path.Count; i++)
        {
            float segment = Vector2.Distance(path[i - 1], path[i]);
            if (travelled + segment >= distance || i == path.Count - 1)
            {
                float t = segment <= 0.0001f
                    ? 0f
                    : Mathf.Clamp01((distance - travelled) / segment);
                return Vector2.Lerp(path[i - 1], path[i], t);
            }

            travelled += segment;
        }

        return path[path.Count - 1];
    }

    /// <summary>
    /// Builds a round dot: a filled centre in <paramref name="fillColor"/> with
    /// a border in <paramref name="outlineColor"/> around it.
    /// </summary>
    private static Texture2D BuildDotTexture(Color fillColor, Color outlineColor,
        out float contentFraction)
    {
        int size = DotTextureSize;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "TrajectoryDot"
        };

        Color32[] pixels = new Color32[size * size];
        Color32 clear = new Color32(255, 255, 255, 0);
        float center = (size - 1) * 0.5f;
        float outer = size * 0.5f;
        float inner = outer * 0.78f;
        int samples = Supersampling * Supersampling;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int insideFill = 0;
                int insideBorder = 0;
                for (int sy = 0; sy < Supersampling; sy++)
                {
                    for (int sx = 0; sx < Supersampling; sx++)
                    {
                        float px = x + (sx + 0.5f) / Supersampling - 0.5f;
                        float py = y + (sy + 0.5f) / Supersampling - 0.5f;
                        float dx = px - center;
                        float dy = py - center;
                        float radius = Mathf.Sqrt(dx * dx + dy * dy);
                        if (radius <= inner)
                        {
                            insideFill++;
                        }

                        if (radius <= outer)
                        {
                            insideBorder++;
                        }
                    }
                }

                if (insideBorder == 0)
                {
                    pixels[y * size + x] = clear;
                    continue;
                }

                float borderCoverage = insideBorder / (float)samples;
                Color border = new Color(outlineColor.r, outlineColor.g,
                    outlineColor.b, outlineColor.a * borderCoverage);
                float fillCoverage = insideFill / (float)samples;
                Color fill = new Color(fillColor.r, fillColor.g, fillColor.b,
                    fillColor.a * fillCoverage);
                pixels[y * size + x] = Blend(fill, border);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        contentFraction = 2f * outer / size;
        return texture;
    }

    private static Color32 Blend(Color over, Color under)
    {
        float alpha = over.a + under.a * (1f - over.a);
        if (alpha <= 0.0001f)
        {
            return new Color32(0, 0, 0, 0);
        }

        float r = (over.r * over.a + under.r * under.a * (1f - over.a)) / alpha;
        float g = (over.g * over.a + under.g * under.a * (1f - over.a)) / alpha;
        float b = (over.b * over.a + under.b * under.a * (1f - over.a)) / alpha;
        return new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(r * 255f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(g * 255f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255));
    }
}
