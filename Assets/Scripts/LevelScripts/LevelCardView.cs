using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Карточка уже находится в сцене; обновляются только её состояние и подписи.
// При наведении курсора на карточку показывается обводка уровня (hoverOutline).
public sealed class LevelCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private int levelId;
    [SerializeField, Min(0)] private int pageIndex;
    [SerializeField] private Button button;
    [SerializeField] private Image preview;
    [SerializeField] private GameObject currentHighlight;
    [SerializeField] private GameObject lockOverlay;
    [SerializeField] private GameObject soonOverlay;
    [SerializeField] private TMP_Text parHint;
    [SerializeField] private Image[] stars;
    [SerializeField] private Outline hoverOutline;
    public int LevelId => levelId;
    public int PageIndex => pageIndex;

    private bool pointerInside;

    public void Refresh(Sprite starOn, Sprite starOff)
    {
        LevelDefinition level = LevelProgress.GetLevel(levelId);
        bool unlocked = LevelProgress.IsUnlocked(levelId);
        button.interactable = unlocked;
        preview.color = unlocked ? Color.white : (Color)new Color32(140, 140, 140, 255);
        currentHighlight.SetActive(levelId == LevelProgress.CurrentLevelId);
        lockOverlay.SetActive(!unlocked);
        soonOverlay.SetActive(unlocked && !level.CanLoad);
        parHint.text = unlocked ? "3 ЗВЕЗДЫ — ДО " + level.ThreeStarShots
            + (level.ThreeStarShots == 1 ? " ВЫСТРЕЛА" : " ВЫСТРЕЛОВ") : string.Empty;
        int earned = LevelProgress.GetStars(levelId);
        for (int i = 0; i < stars.Length; i++)
            stars[i].sprite = i < earned ? starOn : starOff;
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

    private void OnEnable()
    {
        ApplyHoverState();
    }

    private void OnDisable()
    {
        pointerInside = false;
        ApplyHoverState();
    }

    // Обводка включается только при реальном наведении указателя на карточку.
    private void ApplyHoverState()
    {
        if (hoverOutline != null)
            hoverOutline.enabled = pointerInside;
    }
}
