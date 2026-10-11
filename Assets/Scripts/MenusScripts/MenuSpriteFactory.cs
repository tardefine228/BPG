using UnityEngine;

/// <summary>
/// Builds the small UI sprites the lobby draws in code (paging arrows and page
/// dots) so the menu does not need extra art assets. Works the same way as
/// <see cref="StarSpriteFactory"/>: every icon is generated into a texture at
/// runtime and destroyed together with the menu.
/// </summary>
public static class MenuSpriteFactory
{
    /// <summary>Flat triangle used for the "previous / next page" buttons.</summary>
    public static Sprite CreateArrow(bool pointRight, int size = 128)
    {
        size = Mathf.Max(32, size);
        Texture2D texture = NewTexture(size, pointRight ? "ArrowRight" : "ArrowLeft");
        Color32[] pixels = new Color32[size * size];
        Color32 fill = new Color32(255, 255, 255, 255);
        Color32 clear = new Color32(255, 255, 255, 0);

        float center = (size - 1) * 0.5f;
        float tail = size * 0.20f;
        float tip = size * 0.80f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 0 at the point of the arrow, 1 at its wide tail edge.
                float t = (tip - x) / (tip - tail);
                float allowedHalf = t * size * 0.34f;
                bool inside = t >= 0f && t <= 1f && Mathf.Abs(y - center) <= allowedHalf;
                pixels[y * size + x] = inside ? fill : clear;
            }
        }

        if (!pointRight)
        {
            MirrorHorizontally(pixels, size);
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return ToSprite(texture, texture.name);
    }

    /// <summary>Filled circle used for the active page dot.</summary>
    public static Sprite CreateCircle(int size = 64)
    {
        size = Mathf.Max(16, size);
        Texture2D texture = NewTexture(size, "DotFilled");
        Color32[] pixels = new Color32[size * size];
        Color32 fill = new Color32(255, 255, 255, 255);
        Color32 clear = new Color32(255, 255, 255, 0);
        FillCircle(pixels, size, size * 0.5f, fill, clear);
        texture.SetPixels32(pixels);
        texture.Apply();
        return ToSprite(texture, texture.name);
    }

    /// <summary>Circle outline used for the inactive page dots.</summary>
    public static Sprite CreateRing(int size = 64, float thickness = 0.34f)
    {
        size = Mathf.Max(16, size);
        Texture2D texture = NewTexture(size, "DotRing");
        Color32[] pixels = new Color32[size * size];
        Color32 fill = new Color32(255, 255, 255, 255);
        Color32 clear = new Color32(255, 255, 255, 0);
        FillCircle(pixels, size, size * 0.5f, fill, clear);

        Color32[] inner = new Color32[size * size];
        FillCircle(inner, size, size * (0.5f - Mathf.Clamp(thickness, 0.05f, 0.45f)), fill, clear);

        for (int i = 0; i < pixels.Length; i++)
        {
            if (inner[i].a > 0)
            {
                pixels[i] = clear;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return ToSprite(texture, texture.name);
    }

    private static void FillCircle(Color32[] pixels, int size, float radius, Color32 fill, Color32 clear)
    {
        float center = (size - 1) * 0.5f;
        float sqrRadius = radius * radius;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                pixels[y * size + x] = dx * dx + dy * dy <= sqrRadius ? fill : clear;
            }
        }
    }

    private static void MirrorHorizontally(Color32[] pixels, int size)
    {
        for (int y = 0; y < size; y++)
        {
            int row = y * size;
            for (int x = 0; x < size / 2; x++)
            {
                Color32 temp = pixels[row + x];
                pixels[row + x] = pixels[row + size - 1 - x];
                pixels[row + size - 1 - x] = temp;
            }
        }
    }

    private static Texture2D NewTexture(int size, string name)
    {
        return new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = name
        };
    }

    private static Sprite ToSprite(Texture2D texture, string name)
    {
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f));
        sprite.name = name;
        return sprite;
    }
}
