using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MenuManager : MonoBehaviour
{
    [SerializeField] private CanvasGroup mainMenuGroup;
    [SerializeField] private CanvasGroup settingsGroup;
    [SerializeField] private CanvasGroup levelSelectGroup;
    [SerializeField] private float fadeDuration = 0.18f;

    private PanelFade settingsFade;
    private PanelFade levelsFade;
    private LevelSelectController levelSelect;
    public bool SettingsOpen => settingsFade != null && settingsFade.IsVisible;
    public bool LevelsOpen => levelsFade != null && levelsFade.IsVisible;

    private void Start()
    {
        Time.timeScale = 1f;
        InitializePanels();
#if UNITY_ANDROID
        Application.wantsMobileBackButton = true;
#endif
    }

    private bool InitializePanels()
    {
        if (settingsFade != null && levelsFade != null)
            return true;

        Canvas canvas = mainMenuGroup != null ? mainMenuGroup.GetComponentInParent<Canvas>() : null;
        if (canvas == null || settingsGroup == null)
        {
            Debug.LogError("MenuManager needs the main-menu CanvasGroup and settings CanvasGroup.", this);
            return false;
        }

        if (levelSelectGroup == null)
        {
            Transform panel = canvas.transform.Find("LevelSelectPanel");
            if (panel == null)
            {
                // Also supports a menu scene already open before the panel was added.
                GameObject panelObject = new GameObject("LevelSelectPanel", typeof(RectTransform), typeof(CanvasGroup));
                panelObject.layer = canvas.gameObject.layer;
                panelObject.SetActive(false);
                RectTransform rect = panelObject.GetComponent<RectTransform>();
                rect.SetParent(canvas.transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.sizeDelta = Vector2.zero;
                panel = rect;
            }
            levelSelectGroup = panel.GetComponent<CanvasGroup>();
            if (levelSelectGroup == null)
                levelSelectGroup = panel.gameObject.AddComponent<CanvasGroup>();
        }

        levelSelect = levelSelectGroup.GetComponent<LevelSelectController>();
        if (levelSelect == null)
            levelSelect = levelSelectGroup.gameObject.AddComponent<LevelSelectController>();
        levelSelect.Initialize(this);
        settingsFade = ConfigureFade(settingsGroup);
        levelsFade = ConfigureFade(levelSelectGroup);
        settingsFade.SetVisible(false, true);
        levelsFade.SetVisible(false, true);
        return true;
    }

    private PanelFade ConfigureFade(CanvasGroup group)
    {
        PanelFade fade = group.GetComponent<PanelFade>();
        if (fade == null)
            fade = group.gameObject.AddComponent<PanelFade>();
        fade.duration = fadeDuration;
        return fade;
    }

    public void OpenSettings()
    {
        if (SceneTransition.IsLoading || !InitializePanels() || SettingsOpen || LevelsOpen)
            return;
        SetMainMenuEnabled(false);
        settingsFade.SetVisible(true);
    }

    public void CloseSettings()
    {
        if (!SettingsOpen)
            return;
        settingsFade.SetVisible(false);
        SetMainMenuEnabled(true);
    }

    public void OpenLevels()
    {
        if (SceneTransition.IsLoading || !InitializePanels() || LevelsOpen || SettingsOpen)
            return;
        SetMainMenuEnabled(false);
        levelsFade.SetVisible(true);
    }

    public void CloseLevels()
    {
        if (!LevelsOpen)
            return;
        levelsFade.SetVisible(false);
        SetMainMenuEnabled(true);
    }

    private void Update()
    {
        if (SceneTransition.IsLoading)
            return;
#if ENABLE_INPUT_SYSTEM
        bool escapePressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        bool escapePressed = Input.GetKeyDown(KeyCode.Escape);
#endif
        if (!escapePressed)
            return;
        if (SettingsOpen)
        {
            AudioManager.Instance?.PlayUiClose();
            CloseSettings();
        }
        else if (LevelsOpen)
        {
            AudioManager.Instance?.PlayUiClose();
            if (!levelSelect.DismissComingSoon())
                CloseLevels();
        }
    }

    private void SetMainMenuEnabled(bool enabled)
    {
        mainMenuGroup.interactable = enabled;
        mainMenuGroup.blocksRaycasts = enabled;
    }
}
