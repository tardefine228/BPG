using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Added automatically to menu controls, including inactive settings pages.
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
        if (button != null && MenuButtonStyle.Default != null)
            MenuButtonStyle.Default.Apply(button);
    }

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
        // The action may disable the button or load a scene before this listener runs.
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
