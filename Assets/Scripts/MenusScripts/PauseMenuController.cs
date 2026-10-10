using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Builds and controls the in-game pause menu. The UI is created at runtime so
/// the gameplay scene can keep its existing presentation and layout.
/// </summary>
[DisallowMultipleComponent]
public sealed class PauseMenuController : MonoBehaviour
{
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private SlingshotController gameplayController;

    private const float DefaultVolume = 0.5f;
    private const int PauseCanvasSortingOrder = 100;

    private GameObject overlayObject;
    private GameObject pausePage;
    private GameObject settingsPage;
    private Button pauseButton;
    private Slider musicVolumeSlider;
    private Slider effectsVolumeSlider;
    private AudioManager audioManager;
    private Sprite uiSprite;
    private TMP_FontAsset uiFont;
    private bool isPaused;
    private bool audioCallbacksBound;
    private float timeScaleBeforePause = 1f;

    private void Awake()
    {
        if (gameplayController == null)
        {
            gameplayController = GetComponent<SlingshotController>();
        }

        // Avoid carrying a paused time scale into this scene if it was reloaded.
        Time.timeScale = 1f;
        BuildPauseInterface();
    }

    private void Start()
    {
        BindAudioSettings();
    }

    private void Update()
    {
        if (!PauseKeyPressed())
        {
            return;
        }

        if (!isPaused)
        {
            OpenPauseMenu();
        }
        else if (settingsPage != null && settingsPage.activeSelf)
        {
            ShowPausePage();
        }
        else
        {
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

        if (uiSprite != null)
        {
            Destroy(uiSprite);
        }
    }

    public void OpenPauseMenu()
    {
        if (isPaused)
        {
            return;
        }

        timeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
        isPaused = true;

        if (gameplayController != null)
        {
            gameplayController.CancelInput();
            gameplayController.enabled = false;
        }

        Time.timeScale = 0f;
        pauseButton.interactable = false;
        overlayObject.SetActive(true);
        ShowPausePage();
    }

    public void ResumeGame()
    {
        if (!isPaused)
        {
            return;
        }

        isPaused = false;
        overlayObject.SetActive(false);
        pauseButton.interactable = true;
        Time.timeScale = timeScaleBeforePause > 0f ? timeScaleBeforePause : 1f;

        if (gameplayController != null)
        {
            gameplayController.enabled = true;
        }
    }

    public void OpenSettings()
    {
        if (!isPaused)
        {
            return;
        }

        BindAudioSettings();
        pausePage.SetActive(false);
        settingsPage.SetActive(true);
    }

    public void ReturnToPausePage()
    {
        ShowPausePage();
    }

    public void ReturnToMainMenu()
    {
        isPaused = false;
        Time.timeScale = 1f;

        if (gameplayController != null)
        {
            gameplayController.enabled = true;
        }

        SceneManager.LoadScene(mainMenuSceneName);
    }

    private void ShowPausePage()
    {
        if (pausePage != null)
        {
            pausePage.SetActive(true);
        }

        if (settingsPage != null)
        {
            settingsPage.SetActive(false);
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

    private void BuildPauseInterface()
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0)
        {
            uiLayer = 5;
        }

        GameObject canvasObject = new GameObject(
            "GameplayPauseCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.layer = uiLayer;

        Canvas pauseCanvas = canvasObject.GetComponent<Canvas>();
        pauseCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        pauseCanvas.sortingOrder = PauseCanvasSortingOrder;

        CanvasScaler canvasScaler = canvasObject.GetComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasScaler.matchWidthOrHeight = 0.5f;

        // Create a plain white UI sprite from Unity's built-in white texture;
        // relying on UI/Skin/UISprite.psd fails in newer Unity editor versions.
        uiSprite = Sprite.Create(
            Texture2D.whiteTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f));
        uiFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        pauseButton = CreateButton(
            canvasObject.transform,
            "PauseButton",
            "ПАУЗА",
            new Vector2(150f, 76f),
            new Vector2(-38f, -38f),
            OpenPauseMenu,
            fontSize: 30f,
            topRight: true);
        RectTransform overlayRect = CreateRectTransform(
            "PauseOverlay",
            canvasObject.transform,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero);
        Image overlayImage = overlayRect.gameObject.AddComponent<Image>();
        ConfigureImage(overlayImage, new Color32(0, 0, 0, 205));
        overlayImage.raycastTarget = true;
        overlayObject = overlayRect.gameObject;

        RectTransform pausePageRect = CreateRectTransform(
            "PausePage",
            overlayObject.transform,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero);
        pausePage = pausePageRect.gameObject;
        BuildPausePage(pausePageRect);

        RectTransform settingsPageRect = CreateRectTransform(
            "SettingsPage",
            overlayObject.transform,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero);
        settingsPage = settingsPageRect.gameObject;
        BuildSettingsPage(settingsPageRect);
        settingsPage.SetActive(false);

        overlayObject.SetActive(false);
    }

    private void BuildPausePage(RectTransform parent)
    {
        CreateLabel(parent, "PauseTitle", "ПАУЗА", new Vector2(0f, 260f),
            new Vector2(800f, 90f), 58f, Color.white);

        CreateButton(parent, "ResumeButton", "ОСТАТЬСЯ",
            new Vector2(640f, 88f), new Vector2(0f, 105f), ResumeGame, 42f);
        CreateButton(parent, "SettingsButton", "НАСТРОЙКИ",
            new Vector2(640f, 88f), new Vector2(0f, -5f), OpenSettings, 42f);
        CreateButton(parent, "MainMenuButton", "В МЕНЮ",
            new Vector2(640f, 88f), new Vector2(0f, -115f), ReturnToMainMenu, 42f);
    }

    private void BuildSettingsPage(RectTransform parent)
    {
        CreateLabel(parent, "SettingsTitle", "НАСТРОЙКИ", new Vector2(0f, 275f),
            new Vector2(900f, 90f), 54f, Color.white);

        CreateLabel(parent, "MusicVolumeLabel", "ГРОМКОСТЬ МУЗЫКИ",
            new Vector2(-335f, 125f), new Vector2(640f, 72f), 38f, Color.white);
        musicVolumeSlider = CreateVolumeSlider(
            parent,
            "MusicVolumeSlider",
            new Vector2(345f, 125f),
            new Vector2(610f, 58f),
            DefaultVolume);

        CreateLabel(parent, "EffectsVolumeLabel", "ГРОМКОСТЬ ЭФФЕКТОВ",
            new Vector2(-335f, 20f), new Vector2(640f, 72f), 38f, Color.white);
        effectsVolumeSlider = CreateVolumeSlider(
            parent,
            "EffectsVolumeSlider",
            new Vector2(345f, 20f),
            new Vector2(610f, 58f),
            DefaultVolume);

        CreateButton(parent, "BackToPauseButton", "НАЗАД",
            new Vector2(480f, 82f), new Vector2(0f, -230f), ReturnToPausePage, 40f);
    }

    private Button CreateButton(
        Transform parent,
        string objectName,
        string label,
        Vector2 size,
        Vector2 position,
        UnityAction onClick,
        float fontSize,
        bool topRight = false)
    {
        Vector2 anchor = topRight ? Vector2.one : new Vector2(0.5f, 0.5f);
        Vector2 pivot = topRight ? Vector2.one : new Vector2(0.5f, 0.5f);
        RectTransform rect = CreateRectTransform(
            objectName,
            parent,
            anchor,
            anchor,
            pivot,
            size,
            position);

        Image background = rect.gameObject.AddComponent<Image>();
        ConfigureImage(background, Color.white);

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = CreateButtonColors();
        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        button.onClick.AddListener(onClick);

        RectTransform labelRect = CreateRectTransform(
            "Label",
            rect,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            new Vector2(-18f, -10f),
            Vector2.zero);
        CreateText(labelRect, label, fontSize, new Color32(45, 45, 45, 255));
        return button;
    }

    private TMP_Text CreateLabel(
        Transform parent,
        string objectName,
        string value,
        Vector2 position,
        Vector2 size,
        float fontSize,
        Color color)
    {
        RectTransform rect = CreateRectTransform(
            objectName,
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            size,
            position);

        return CreateText(rect, value, fontSize, color);
    }

    private TMP_Text CreateText(RectTransform parent, string value, float fontSize, Color color)
    {
        TextMeshProUGUI text = parent.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = uiFont;
        text.fontSize = fontSize;
        text.fontSizeMin = Mathf.Max(18f, fontSize - 12f);
        text.fontSizeMax = fontSize;
        text.enableAutoSizing = true;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.raycastTarget = false;
        text.margin = new Vector4(6f, 2f, 6f, 2f);
        return text;
    }

    private Slider CreateVolumeSlider(
        Transform parent,
        string objectName,
        Vector2 position,
        Vector2 size,
        float initialValue)
    {
        RectTransform sliderRect = CreateRectTransform(
            objectName,
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            size,
            position);

        RectTransform trackRect = CreateRectTransform(
            "Track",
            sliderRect,
            new Vector2(0f, 0.38f),
            new Vector2(1f, 0.62f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero);
        Image trackImage = trackRect.gameObject.AddComponent<Image>();
        ConfigureImage(trackImage, new Color32(220, 220, 220, 255));

        RectTransform fillRect = CreateRectTransform(
            "Fill",
            trackRect,
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, 0.5f),
            Vector2.zero,
            Vector2.zero);
        Image fillImage = fillRect.gameObject.AddComponent<Image>();
        ConfigureImage(fillImage, new Color32(230, 155, 82, 255));
        fillImage.raycastTarget = false;

        RectTransform handleArea = CreateRectTransform(
            "HandleSlideArea",
            sliderRect,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            new Vector2(-24f, 0f),
            Vector2.zero);
        RectTransform handleRect = CreateRectTransform(
            "Handle",
            handleArea,
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0.5f, 0.5f),
            new Vector2(28f, 0f),
            Vector2.zero);
        Image handleImage = handleRect.gameObject.AddComponent<Image>();
        ConfigureImage(handleImage, Color.white);

        Slider slider = sliderRect.gameObject.AddComponent<Slider>();
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        slider.transition = Selectable.Transition.ColorTint;
        slider.colors = CreateButtonColors();
        slider.targetGraphic = handleImage;
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.SetValueWithoutNotify(Mathf.Clamp01(initialValue));
        return slider;
    }

    private RectTransform CreateRectTransform(
        string objectName,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 size,
        Vector2 anchoredPosition)
    {
        GameObject uiObject = new GameObject(objectName, typeof(RectTransform));
        uiObject.layer = parent.gameObject.layer;

        RectTransform rect = uiObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        return rect;
    }

    private void ConfigureImage(Image image, Color color)
    {
        image.sprite = uiSprite;
        image.type = Image.Type.Simple;
        image.color = color;
        image.raycastTarget = true;
    }

    private static ColorBlock CreateButtonColors()
    {
        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color32(245, 245, 245, 255);
        colors.pressedColor = new Color32(200, 200, 200, 255);
        colors.selectedColor = new Color32(245, 245, 245, 255);
        colors.disabledColor = new Color32(200, 200, 200, 128);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.1f;
        return colors;
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
