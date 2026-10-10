using TMPro;
using UnityEngine;
using UnityEngine.Events;
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

    public Button CreateButton(Transform parent, string name, string label, Vector2 anchor,
        Vector2 size, Vector2 position, UnityAction onClick, float fontSize)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        obj.layer = parent.gameObject.layer;
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        Button button = obj.GetComponent<Button>();
        button.targetGraphic = obj.GetComponent<Image>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(onClick);
        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.layer = obj.layer;
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.sizeDelta = new Vector2(-18f, -10f);
        TextMeshProUGUI text = labelObject.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = fontSize;
        text.fontSizeMin = Mathf.Max(18f, fontSize - 12f);
        text.fontSizeMax = fontSize;
        text.enableAutoSizing = true;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        Apply(button);
        obj.AddComponent<MenuControlFeedback>();
        return button;
    }
}
