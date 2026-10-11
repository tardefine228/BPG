using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MenuManager : MonoBehaviour
{
    private static bool openLevelsOnArrival;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetNavigation() => openLevelsOnArrival = false;

    // Запрос действует только для следующего открытия главного меню.
    public static void ReturnToLevelSelect()
    {
        if (SceneTransition.IsLoading) return;
        openLevelsOnArrival = true;
        SceneTransition.Load(LevelProgress.MainMenuSceneName);
    }

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
        bool showLevels = openLevelsOnArrival;
        openLevelsOnArrival = false;
        if (InitializePanels() && showLevels)
        {
            // Панель готовится под экраном загрузки, до появления нового меню.
            SetMainMenuEnabled(false);
            levelsFade.SetVisible(true, true);
        }
#if UNITY_ANDROID
        Application.wantsMobileBackButton = true;
#endif
    }

    private bool InitializePanels()
    {
        if (settingsFade != null && levelsFade != null)
            return true;
        if (mainMenuGroup == null || settingsGroup == null || levelSelectGroup == null)
        {
            Debug.LogError("Назначьте три панели меню в Inspector.", this);
            return false;
        }
        settingsFade = settingsGroup.GetComponent<PanelFade>();
        levelsFade = levelSelectGroup.GetComponent<PanelFade>();
        levelSelect = levelSelectGroup.GetComponent<LevelSelectController>();
        levelSelect.Initialize(this);
        settingsFade.duration = levelsFade.duration = fadeDuration;
        settingsFade.SetVisible(false, true);
        levelsFade.SetVisible(false, true);
        return true;
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
