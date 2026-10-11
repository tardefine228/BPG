using TMPro;
using UnityEngine;
using UnityEngine.UI;

[CreateAssetMenu(menuName = "UI/Menu Button Style")]
public sealed class MenuButtonStyle : ScriptableObject
{
    public Sprite buttonSprite;
    public TMP_FontAsset font;
    public Color buttonColor = Color.white;
    public Color textColor = new Color32(50, 50, 50, 255);
    public Color highlightedColor = new Color32(245, 245, 245, 255);
    public Color pressedColor = new Color32(200, 200, 200, 255);
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
        normalColor = Color.white, highlightedColor = highlightedColor,
        selectedColor = highlightedColor, pressedColor = pressedColor,
        disabledColor = disabledColor, colorMultiplier = 1f, fadeDuration = 0.1f
    };

    public void Apply(Button button)
    {
        button.transition = Selectable.Transition.ColorTint;
        button.colors = Colors;
        if (button.targetGraphic is Image image)
        {
            image.sprite = buttonSprite;
            image.type = Image.Type.Sliced;
            image.color = buttonColor;
        }
        foreach (TMP_Text text in button.GetComponentsInChildren<TMP_Text>(true))
        {
            text.font = font;
            text.color = textColor;
            text.fontStyle = FontStyles.Bold;
        }
    }

}
