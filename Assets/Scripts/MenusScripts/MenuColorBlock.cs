using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared button colors used by the main menu, pause overlay and level lobby.
/// </summary>
public static class MenuColorBlock
{
    public static ColorBlock Create()
    {
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color32(245, 245, 245, 255);
        colors.pressedColor = new Color32(200, 200, 200, 255);
        colors.selectedColor = new Color32(245, 245, 245, 255);
        colors.disabledColor = new Color32(210, 210, 210, 255);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.1f;
        return colors;
    }
}
