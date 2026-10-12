using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Hover frame is drawn above card content and does not intercept mouse events.
public sealed class LevelCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    ISelectHandler, IDeselectHandler
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
    [SerializeField] private bool sharedFocusColor = true;
    [SerializeField] private Color hoverFrameColor = new Color32(235, 150, 70, 255);
    [SerializeField, Min(1f)] private float hoverFrameWidth = 3f;

    public int LevelId => levelId;
    public int PageIndex => pageIndex;

    private GameObject hoverFrame;
    private bool pointerInside;
    private bool selected;

    private void Awake()
    {
        if (hoverOutline != null) hoverOutline.enabled = false;
        if (sharedFocusColor && MenuButtonStyle.Default != null)
            hoverFrameColor = MenuButtonStyle.Default.highlightedColor;
        currentHighlight.SetActive(false);
        // Цвет всей карточки не зависит от состояния Button или доступности уровня.
        button.transition = Selectable.Transition.None;
        if (button.targetGraphic != null) button.targetGraphic.color = Color.white;
        CreateHoverFrame();
    }

    private void CreateHoverFrame()
    {
        hoverFrame = PixelFocusFrame.Create((RectTransform)transform, hoverFrameColor, hoverFrameWidth);
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

    public void OnSelect(BaseEventData eventData)
    {
        selected = true;
        ApplyHoverState();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        selected = false;
        ApplyHoverState();
    }

    private void OnEnable()
    {
        selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
        ApplyHoverState();
    }
    private void OnDisable()
    {
        pointerInside = false;
        selected = false;
        ApplyHoverState();
    }

    private void ApplyHoverState()
    {
        if (hoverFrame != null) hoverFrame.SetActive((pointerInside || selected) && isActiveAndEnabled
            && button != null && button.IsInteractable());
    }
}
