using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

// Карточка уже находится в сцене; обновляются только её состояние и подписи.
// Обводка появляется, пока курсор находится над открытой карточкой, и исчезает, когда он уходит.
public sealed class LevelCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private int levelId;
    [SerializeField, Min(0)] private int pageIndex;
    [SerializeField] private Button button;
    [SerializeField] private Image preview;
    [Tooltip("Обводка карточки: видна только при наведении курсора на открытый уровень.")]
    [FormerlySerializedAs("currentHighlight")]
    [SerializeField] private GameObject hoverHighlight;
    [SerializeField] private GameObject lockOverlay;
    [SerializeField] private GameObject soonOverlay;
    [SerializeField] private TMP_Text parHint;
    [SerializeField] private Image[] stars;
    private bool hovered;
    public int LevelId => levelId;
    public int PageIndex => pageIndex;

    public void Refresh(Sprite starOn, Sprite starOff)
    {
        LevelDefinition level = LevelProgress.GetLevel(levelId);
        bool unlocked = LevelProgress.IsUnlocked(levelId);
        button.interactable = unlocked;
        preview.color = unlocked ? Color.white : (Color)new Color32(140, 140, 140, 255);
        lockOverlay.SetActive(!unlocked);
        soonOverlay.SetActive(unlocked && !level.CanLoad);
        parHint.text = unlocked ? "3 ЗВЕЗДЫ — ДО " + level.ThreeStarShots
            + (level.ThreeStarShots == 1 ? " ВЫСТРЕЛА" : " ВЫСТРЕЛОВ") : string.Empty;
        int earned = LevelProgress.GetStars(levelId);
        for (int i = 0; i < stars.Length; i++)
            stars[i].sprite = i < earned ? starOn : starOff;
        UpdateHighlight();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        UpdateHighlight();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        UpdateHighlight();
    }

    // Закрытое меню не оставляет обводку на карточке до следующего наведения.
    private void OnDisable()
    {
        hovered = false;
        UpdateHighlight();
    }

    private void UpdateHighlight()
    {
        if (hoverHighlight != null)
            hoverHighlight.SetActive(hovered && button != null && button.interactable);
    }
}
