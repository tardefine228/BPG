using TMPro;
using UnityEngine;
using UnityEngine.UI;

[CreateAssetMenu(menuName = "UI/Menu Button Style")]
public sealed class MenuButtonStyle : ScriptableObject
{
    public enum Plate { Primary, Secondary, Light, Icon }
    public Sprite buttonSprite;
    public Sprite secondaryButtonSprite;
    public Sprite lightButtonSprite;
    public Sprite iconButtonSprite;
    public Sprite panelSprite;
    public Sprite illustrationPanelSprite;
    public TMP_FontAsset font;
    public Color buttonColor = Color.white;
    public Color textColor = new Color32(244, 235, 221, 255);
    public Color darkTextColor = new Color32(34, 27, 49, 255);
    [Tooltip("Цвет только рамки фокуса; заливка при наведении остаётся прежней.")]
    public Color highlightedColor = new Color32(235, 150, 70, 255);
    [Min(1f)] public float focusFrameWidth = 3f;
    public Color disabledColor = new Color32(200, 200, 200, 128);

    private static MenuButtonStyle defaultStyle;
    public static MenuButtonStyle Default
    {
        get
        {
            if (defaultStyle == null)
                defaultStyle = Resources.Load<MenuButtonStyle>("MenuButtonStyle");
            return defaultStyle;
        }
    }

    public ColorBlock Colors => new ColorBlock
    {
        normalColor = Color.white, highlightedColor = Color.white,
        selectedColor = Color.white, pressedColor = Color.white,
        disabledColor = disabledColor, colorMultiplier = 1f, fadeDuration = 0.1f
    };

    public void Apply(Button button, Plate plate = Plate.Primary)
    {
        button.transition = Selectable.Transition.ColorTint;
        button.colors = Colors;
        if (button.targetGraphic is Image image)
        {
            image.sprite = GetSprite(plate);
            image.type = Image.Type.Sliced;
            image.color = buttonColor;
        }
        foreach (TMP_Text text in button.GetComponentsInChildren<TMP_Text>(true))
        {
            ApplyFont(text);
            text.color = plate == Plate.Light || plate == Plate.Primary || plate == Plate.Icon
                ? darkTextColor : textColor;
        }
    }

    public Sprite GetSprite(Plate plate) => plate == Plate.Secondary ? secondaryButtonSprite
        : plate == Plate.Light ? lightButtonSprite : plate == Plate.Icon ? iconButtonSprite : buttonSprite;

    public void ApplyFont(TMP_Text text)
    {
        if (text == null || font == null) return;
        text.font = font;
        text.fontSharedMaterial = font.material;
        text.fontStyle = FontStyles.Normal;
    }

    // Focus is a separate frame. No warm tint is applied to buttons or slider handles.
    public void ApplyFocus(Selectable control)
    {
        if (control == null) return;
        ColorBlock colors = control.colors;
        colors.highlightedColor = colors.normalColor;
        colors.selectedColor = colors.normalColor;
        colors.pressedColor = colors.normalColor;
        colors.fadeDuration = 0.1f;
        control.colors = colors;
        control.transition = Selectable.Transition.ColorTint;
    }

}
