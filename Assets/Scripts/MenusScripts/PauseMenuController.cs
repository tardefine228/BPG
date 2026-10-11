using UnityEngine;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Управляет готовыми панелями паузы и настроек, сохранёнными в сцене.
[DisallowMultipleComponent]
public sealed class PauseMenuController : MonoBehaviour
{
    [SerializeField] private SlingshotController gameplayController;

    [SerializeField] private GameObject pausePage;
    [SerializeField] private GameObject settingsPage;
    [SerializeField] private Button pauseButton;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private Slider effectsVolumeSlider;
    private AudioManager audioManager;
    private bool isPaused;
    private bool audioCallbacksBound;
    private float timeScaleBeforePause = 1f;
    [SerializeField] private PanelFade overlayFade;
    [SerializeField] private PanelFade pauseFade;
    [SerializeField] private PanelFade settingsFade;
    private bool gameplayWasEnabled;
    private bool resultScreenOpen;

    public void SetResultScreenOpen()
    {
        if (isPaused)
            ResumeGame();
        resultScreenOpen = true;
        if (pauseButton != null)
            pauseButton.gameObject.SetActive(false);
    }

    private void Awake()
    {
        if (gameplayController == null)
        {
            gameplayController = GetComponent<SlingshotController>();
        }

        // Перезагрузка уровня восстанавливает нормальную скорость игрового времени.
        Time.timeScale = 1f;
        settingsFade.SetVisible(false, true);
        pauseFade.SetVisible(false, true);
        overlayFade.SetVisible(false, true);
    }

    private void Start()
    {
        BindAudioSettings();
    }

    private void Update()
    {
        if (resultScreenOpen || SceneTransition.IsLoading || !PauseKeyPressed())
        {
            return;
        }

        if (!isPaused)
        {
            AudioManager.Instance?.PlayUiOpen();
            OpenPauseMenu();
        }
        else if (settingsFade.IsVisible)
        {
            AudioManager.Instance?.PlayUiClose();
            ShowPausePage();
        }
        else
        {
            AudioManager.Instance?.PlayUiClose();
            ResumeGame();
        }
    }

    private void OnDestroy()
    {
        if (audioCallbacksBound && audioManager != null)
        {
            if (musicVolumeSlider != null)
            {
                musicVolumeSlider.onValueChanged.RemoveListener(audioManager.SetMusicVolume);
            }

            if (effectsVolumeSlider != null)
            {
                effectsVolumeSlider.onValueChanged.RemoveListener(audioManager.SetEffectsVolume);
            }
        }

        if (isPaused)
        {
            Time.timeScale = timeScaleBeforePause > 0f ? timeScaleBeforePause : 1f;
        }

    }

    public void OpenPauseMenu()
    {
        if (resultScreenOpen || isPaused || SceneTransition.IsLoading)
        {
            return;
        }

        timeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
        isPaused = true;

        if (gameplayController != null)
        {
            gameplayWasEnabled = gameplayController.enabled;
            gameplayController.CancelInput();
            gameplayController.enabled = false;
        }

        Time.timeScale = 0f;
        if (pauseButton != null)
        {
            pauseButton.interactable = false;
        }
        overlayFade.SetVisible(true);
        ShowPausePage();
    }

    public void ResumeGame()
    {
        if (!isPaused)
        {
            return;
        }

        isPaused = false;
        overlayFade.SetVisible(false);
        if (pauseButton != null)
        {
            pauseButton.interactable = true;
        }

        Time.timeScale = timeScaleBeforePause > 0f ? timeScaleBeforePause : 1f;

        if (gameplayController != null)
        {
            gameplayController.enabled = gameplayWasEnabled;
        }
    }

    public void OpenSettings()
    {
        if (!isPaused)
        {
            return;
        }

        BindAudioSettings();
        pauseFade.SetVisible(false);
        settingsFade.SetVisible(true);
    }

    public void ReturnToPausePage()
    {
        ShowPausePage();
    }

    public void ReturnToMainMenu()
    {
        AudioManager.Instance?.PlayUiClick();
        isPaused = false;
        Time.timeScale = 1f;

        if (gameplayController != null)
        {
            gameplayController.enabled = true;
        }

        SceneTransition.Load(LevelProgress.MainMenuSceneName);
    }

    private void ShowPausePage()
    {
        if (pausePage != null)
        {
            pauseFade.SetVisible(true);
        }

        if (settingsPage != null)
        {
            settingsFade.SetVisible(false);
        }
    }

    private void BindAudioSettings()
    {
        if (audioManager == null)
        {
            audioManager = AudioManager.Instance;
        }

        if (audioManager == null)
        {
            return;
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.SetValueWithoutNotify(audioManager.MusicVolume);
            if (!audioCallbacksBound)
            {
                musicVolumeSlider.onValueChanged.AddListener(audioManager.SetMusicVolume);
            }
        }

        if (effectsVolumeSlider != null)
        {
            effectsVolumeSlider.SetValueWithoutNotify(audioManager.EffectsVolume);
            if (!audioCallbacksBound)
            {
                effectsVolumeSlider.onValueChanged.AddListener(audioManager.SetEffectsVolume);
            }
        }

        audioCallbacksBound = true;
    }

    private static bool PauseKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }
}
