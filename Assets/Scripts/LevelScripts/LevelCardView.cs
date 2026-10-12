using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Hover frame is drawn above card content and does not intercept mouse events.
public sealed class LevelCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private int levelId;
    [SerializeField, Min(0)] private int pageIndex;
    [SerializeField] private Button button;
    [SerializeField] private Image preview;
    [SerializeField] private GameObject currentHighlight;
    [SerializeField] private GameObject lockOverlay;
    [SerializeField] private TMP_Text lockLabel;
    [SerializeField] private GameObject soonOverlay;
    [SerializeField] private TMP_Text parHint;
    [SerializeField] private Image[] stars;
    [SerializeField] private Outline hoverOutline;

    [Header("Рамка при наведении")]
    [SerializeField] private Color hoverFrameColor = new Color32(235, 150, 70, 255);
    [SerializeField, Min(1f)] private float hoverFrameWidth = 7f;

    public int LevelId => levelId;
    public int PageIndex => pageIndex;

    private GameObject hoverFrame;
    private bool pointerInside;

    private void Awake()
    {
        if (hoverOutline != null) hoverOutline.enabled = false;
        currentHighlight.SetActive(false);
        // Цвет всей карточки не зависит от состояния Button или доступности уровня.
        button.transition = Selectable.Transition.None;
        if (button.targetGraphic != null) button.targetGraphic.color = Color.white;
        CreateHoverFrame();
    }

    private void CreateHoverFrame()
    {
        hoverFrame = new GameObject("HoverFrame", typeof(RectTransform));
        hoverFrame.transform.SetParent(transform, false);
        var root = (RectTransform)hoverFrame.transform;
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        root.SetAsLastSibling();

        CreateSide(root, "Top", new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -hoverFrameWidth), Vector2.zero);
        CreateSide(root, "Bottom", Vector2.zero, new Vector2(1, 0),
            Vector2.zero, new Vector2(0, hoverFrameWidth));
        CreateSide(root, "Left", Vector2.zero, new Vector2(0, 1),
            Vector2.zero, new Vector2(hoverFrameWidth, 0));
        CreateSide(root, "Right", new Vector2(1, 0), Vector2.one,
            new Vector2(-hoverFrameWidth, 0), Vector2.zero);
        hoverFrame.SetActive(false);
    }

    private void CreateSide(RectTransform parent, string name, Vector2 anchorMin,
        Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var side = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        side.transform.SetParent(parent, false);
        var rect = (RectTransform)side.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        var image = side.GetComponent<Image>();
        image.color = hoverFrameColor;
        image.raycastTarget = false;
    }

    public void Refresh(Sprite starOn, Sprite starOff)
    {
        LevelDefinition level = LevelProgress.GetLevel(levelId);
        bool unlocked = LevelProgress.IsUnlocked(levelId);
        int requiredStars = LevelProgress.RequiredStarsFor(levelId);
        button.interactable = unlocked;
        preview.color = unlocked ? Color.white : (Color)new Color32(140, 140, 140, 255);
        currentHighlight.SetActive(false);
        lockOverlay.SetActive(!unlocked);
        soonOverlay.SetActive(unlocked && !level.CanLoad);
        // На закрытом уровне подпись прямо говорит, сколько звёзд для него нужно.
        if (lockLabel != null)
            lockLabel.text = requiredStars > 0 ? "НУЖНО " + requiredStars + " ЗВЁЗД" : "ЗАКРЫТО";
        // Под звёздами стоит короткое название уровня, а не подсказка про звёзды
        // за мышей: сама оценка видна по закрашенным звёздам.
        parHint.text = level.Tagline;
        int earned = LevelProgress.GetStars(levelId);
        for (int i = 0; i < stars.Length; i++)
            if (stars[i] != null) stars[i].sprite = i < earned ? starOn : starOff;
        ApplyHoverState();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerInside = true;
        ApplyHoverState();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerInside = false;
        ApplyHoverState();
    }

    private void OnEnable() => ApplyHoverState();
    private void OnDisable()
    {
        pointerInside = false;
        ApplyHoverState();
    }

    private void ApplyHoverState()
    {
        if (hoverFrame != null) hoverFrame.SetActive(pointerInside && isActiveAndEnabled);
    }
}
