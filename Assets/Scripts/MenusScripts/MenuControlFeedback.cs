using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Общий фокус, звуки и лёгкая анимация существующих контролов меню.
[DisallowMultipleComponent]
public sealed class MenuControlFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
{
    [Header("Нажатие и общая рамка фокуса")]
    [SerializeField] private bool pressCompression = true;
    [SerializeField, Range(0f, 0.1f)] private float pressAmount = 0.02f;
    [SerializeField, Min(0.01f)] private float pressReturnDuration = 0.1f;
    [SerializeField] private bool sharedFocusColor = true;
    [SerializeField] private MenuButtonStyle.Plate plate = MenuButtonStyle.Plate.Primary;

    [Header("Лёгкое покачивание кнопки")]
    [SerializeField] private bool idleSway;
    [Tooltip("Максимальный наклон в градусах. Для едва заметного движения: 0.3–0.6.")]
    [SerializeField, Range(0f, 3f)] private float swayAngle = 0.45f;
    [Tooltip("Смещение вверх-вниз в единицах Canvas.")]
    [SerializeField, Range(0f, 5f)] private float swayDistance = 0.7f;
    [Tooltip("Число покачиваний в секунду.")]
    [SerializeField, Range(0.05f, 1f)] private float swayFrequency = 0.18f;

    private Selectable control;
    private Button button;
    private Toggle toggle;
    private RectTransform rect;
    private Quaternion restingRotation;
    private Vector2 restingPosition;
    private Vector3 restingScale;
    private float swayPhase;
    private bool swayApplied;
    private bool pointerHeld;
    private bool compressionApplied;
    private float pressMultiplier = 1f;
    private float releaseFrom;
    private float releaseElapsed;
    private GameObject focusFrame;
    private bool pointerInside;
    private bool selected;

    public MenuButtonStyle.Plate PlateStyle => plate;

    private void Awake()
    {
        control = GetComponent<Selectable>();
        button = control as Button;
        toggle = control as Toggle;
        rect = transform as RectTransform;
        if (sharedFocusColor && GetComponent<LevelCardView>() == null)
        {
            MenuButtonStyle.Default?.ApplyFocus(control);
            if (control != null && rect != null)
            {
                var style = MenuButtonStyle.Default;
                RectTransform frameRect = control is Slider && control.targetGraphic != null
                    ? control.targetGraphic.rectTransform : rect;
                focusFrame = PixelFocusFrame.Create(frameRect,
                    style != null ? style.highlightedColor : (Color)new Color32(235, 150, 70, 255),
                    style != null ? style.focusFrameWidth : 3f);
            }
        }
        // Разные фазы не дают соседним кнопкам двигаться строем.
        swayPhase = Mathf.Repeat((GetEntityId().GetHashCode() & 1023) * 0.618034f, 1f) * Mathf.PI * 2f;
    }

#if UNITY_EDITOR
    // Полный стиль применяется вручную; общие цвета фокуса управляются Shared Focus Color.
    [ContextMenu("Применить MenuButtonStyle к этой кнопке")]
    private void ApplyStyleInEditor()
    {
        Button target = GetComponent<Button>();
        if (target == null || MenuButtonStyle.Default == null) return;
        UnityEditor.Undo.RegisterFullObjectHierarchyUndo(gameObject, "Применить стиль кнопки");
        MenuButtonStyle.Default.Apply(target, plate);
    }
#endif

    private void OnEnable()
    {
        if (rect != null)
        {
            restingRotation = rect.localRotation;
            restingPosition = rect.anchoredPosition;
            restingScale = rect.localScale;
        }
        if (button != null)
            button.onClick.AddListener(PlayClick);
        if (toggle != null)
            toggle.onValueChanged.AddListener(OnToggleChanged);
        selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
        UpdateFocus();
    }

    private void OnDisable()
    {
        RestorePose();
        RestoreCompression();
        pointerInside = selected = false;
        UpdateFocus();
        if (button != null)
            button.onClick.RemoveListener(PlayClick);
        if (toggle != null)
            toggle.onValueChanged.RemoveListener(OnToggleChanged);
    }

    private void LateUpdate()
    {
        UpdateFocus();
        if (!idleSway || button == null || rect == null)
        {
            RestorePose();
        }
        else
        {
            float phase = Time.unscaledTime * swayFrequency * Mathf.PI * 2f + swayPhase;
            rect.localRotation = restingRotation * Quaternion.Euler(0f, 0f, Mathf.Sin(phase) * swayAngle);
            rect.anchoredPosition = restingPosition + Vector2.up * (Mathf.Sin(phase * 0.83f + 1.2f) * swayDistance);
            swayApplied = true;
        }
        if (!compressionApplied) return;
        if (!pressCompression || button == null || !button.IsInteractable())
        {
            RestoreCompression();
            return;
        }
        if (!pointerHeld)
        {
            releaseElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(releaseElapsed / Mathf.Max(0.01f, pressReturnDuration));
            pressMultiplier = Mathf.Lerp(releaseFrom, 1f, Mathf.SmoothStep(0f, 1f, t));
            if (t >= 1f)
            {
                RestoreCompression();
                return;
            }
        }
        rect.localScale = restingScale * pressMultiplier;
    }

    private void RestorePose()
    {
        if (!swayApplied || rect == null) return;
        rect.localRotation = restingRotation;
        rect.anchoredPosition = restingPosition;
        swayApplied = false;
    }

    private void PlayClick()
    {
        // Действие кнопки может скрыть её или начать переход до вызова звука.
        AudioManager.Instance?.PlayUiClick();
        // Keyboard submit also gets a brief squeeze. Closed panels restore their pose in OnDisable.
        if (isActiveAndEnabled && !compressionApplied)
            Compress();
    }

    private void OnToggleChanged(bool value) => PlayClick();

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerInside = true;
        UpdateFocus();
        if (control != null && control.IsInteractable())
            AudioManager.Instance?.PlayUiHover();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (button != null && button.IsInteractable() && eventData.button == PointerEventData.InputButton.Left)
        {
            pointerHeld = true;
            Compress();
        }
        if (control is Slider && control.IsInteractable() && eventData.button == PointerEventData.InputButton.Left)
            PlayClick();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) ReleasePress();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerInside = false;
        UpdateFocus();
        ReleasePress();
    }

    public void OnSelect(BaseEventData eventData)
    {
        selected = true;
        UpdateFocus();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        selected = false;
        UpdateFocus();
    }

    private void UpdateFocus()
    {
        if (focusFrame != null)
            focusFrame.SetActive(sharedFocusColor && isActiveAndEnabled && control != null
                && control.IsInteractable() && (pointerInside || selected));
    }

    private void Compress()
    {
        if (!pressCompression || button == null || rect == null || !button.IsInteractable()) return;
        pressMultiplier = 1f - Mathf.Clamp(pressAmount, 0f, 0.1f);
        releaseFrom = pressMultiplier;
        releaseElapsed = 0f;
        compressionApplied = true;
        rect.localScale = restingScale * pressMultiplier;
    }

    private void ReleasePress()
    {
        if (!pointerHeld) return;
        pointerHeld = false;
        releaseFrom = pressMultiplier;
        releaseElapsed = 0f;
    }

    private void RestoreCompression()
    {
        if (compressionApplied && rect != null) rect.localScale = restingScale;
        pointerHeld = false;
        compressionApplied = false;
        pressMultiplier = 1f;
    }
}
