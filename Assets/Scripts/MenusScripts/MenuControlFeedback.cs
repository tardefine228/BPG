using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Отклик готовых кнопок, переключателей и ползунков: звуки наведения и нажатия.
[DisallowMultipleComponent]
public sealed class MenuControlFeedback : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler
{
    private Selectable control;
    private Button button;
    private Toggle toggle;

    private void Awake()
    {
        control = GetComponent<Selectable>();
        button = control as Button;
        toggle = control as Toggle;
    }

#if UNITY_EDITOR
    // Стиль применяется вручную, чтобы игра не затирала оформление из Inspector.
    [ContextMenu("Применить MenuButtonStyle к этой кнопке")]
    private void ApplyStyleInEditor()
    {
        Button target = GetComponent<Button>();
        if (target == null || MenuButtonStyle.Default == null) return;
        UnityEditor.Undo.RegisterFullObjectHierarchyUndo(gameObject, "Применить стиль кнопки");
        MenuButtonStyle.Default.Apply(target);
    }
#endif

    private void OnEnable()
    {
        if (button != null)
            button.onClick.AddListener(PlayClick);
        if (toggle != null)
            toggle.onValueChanged.AddListener(OnToggleChanged);
    }

    private void OnDisable()
    {
        if (button != null)
            button.onClick.RemoveListener(PlayClick);
        if (toggle != null)
            toggle.onValueChanged.RemoveListener(OnToggleChanged);
    }

    private void PlayClick()
    {
        // Действие кнопки может скрыть её или начать переход до вызова звука.
        AudioManager.Instance?.PlayUiClick();
    }

    private void OnToggleChanged(bool value) => PlayClick();

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (control != null && control.IsInteractable())
            AudioManager.Instance?.PlayUiHover();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (control is Slider && control.IsInteractable() && eventData.button == PointerEventData.InputButton.Left)
            PlayClick();
    }
}
