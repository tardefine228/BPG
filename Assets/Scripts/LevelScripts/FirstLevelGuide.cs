using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

// Показывает обучение первого уровня один раз; подтверждение хранится отдельно от прогресса.
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class FirstLevelGuide : MonoBehaviour
{
    private const string CompletedKey = "BPG.Guide.Gameplay.v1";
    private static FirstLevelGuide current;
    public static bool IsOpen => current != null && current.isShowing;
    public static bool HasBeenCompleted => PlayerPrefs.GetInt(CompletedKey, 0) == 1;

    [SerializeField] private PanelFade overlayFade;
    [SerializeField] private Image firstIllustration;
    [SerializeField] private Image secondIllustration;
    [SerializeField] private SlingshotController gameplayController;
    [SerializeField] private Button pauseButton;
    [Header("Комикс перед обучением")]
    [SerializeField] private Sprite comicSprite;
    [SerializeField] private GameObject tutorialPanel;
    [SerializeField, Min(0f)] private float comicContinueDelay = 2f;
    [SerializeField, Range(0.05f, 0.15f)] private float comicFooterHeight = 0.09f;
    private GameObject comicPanel;
    private TMP_Text comicContinueText;
    private bool comicShowing;
    private float comicReadyAt;
    private int tutorialOpenedFrame = -1;
    private Image overlayImage;
    private Color tutorialBackdrop;
    private bool isShowing;
    private bool closing;
    private bool gameplayWasEnabled;
    private bool pauseWasInteractable;
    private float previousTimeScale;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => current = null;

    private void Awake()
    {
#if UNITY_EDITOR
        // The local marker is ignored by Git and consumed only for this user's next preview.
        string resetMarker = System.IO.Path.Combine(Application.dataPath, "../Temp/BPG-reset-first-guide.once");
        if (System.IO.File.Exists(resetMarker))
        {
            ResetGuide();
            System.IO.File.Delete(resetMarker);
        }
#endif
        current = this;
        overlayImage = overlayFade.GetComponent<Image>();
        if (overlayImage != null) tutorialBackdrop = overlayImage.color;
        if (tutorialPanel == null)
        {
            Transform panel = firstIllustration.transform;
            while (panel.parent != null && panel.parent != overlayFade.transform) panel = panel.parent;
            tutorialPanel = panel.gameObject;
        }
        CreateComicPanel();
        overlayFade.SetVisible(false, true);
        firstIllustration.enabled = firstIllustration.sprite != null;
        secondIllustration.enabled = secondIllustration.sprite != null;
    }

    private IEnumerator Start()
    {
        if (HasBeenCompleted)
            yield break;
        while (SceneTransition.IsLoading)
            yield return null;
        previousTimeScale = Time.timeScale;
        gameplayWasEnabled = gameplayController.enabled;
        pauseWasInteractable = pauseButton.interactable;
        isShowing = true;
        gameplayController.CancelInput();
        gameplayController.enabled = false;
        pauseButton.interactable = false;
        Time.timeScale = 0f;
        ShowComicPage();
        overlayFade.SetVisible(true);
    }

    private void CreateComicPanel()
    {
        if (comicSprite == null) return;
        comicPanel = new GameObject("IntroComicPage", typeof(RectTransform));
        var root = (RectTransform)comicPanel.transform;
        root.SetParent(overlayFade.transform, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = new Vector2(18f, 18f);
        root.offsetMax = new Vector2(-18f, -18f);
        var picture = new GameObject("Comic", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(AspectRatioFitter));
        picture.transform.SetParent(root, false);
        Image image = picture.GetComponent<Image>();
        image.sprite = comicSprite;
        image.raycastTarget = true;
        var aspect = picture.GetComponent<AspectRatioFitter>();
        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        aspect.aspectRatio = comicSprite.rect.width / comicSprite.rect.height;

        // Cover the baked-in caption throughout this page; the replacement appears only when ready.
        var footer = new GameObject("CaptionCover", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        footer.transform.SetParent(picture.transform, false);
        var footerRect = (RectTransform)footer.transform;
        footerRect.anchorMin = Vector2.zero;
        footerRect.anchorMax = new Vector2(1f, comicFooterHeight);
        footerRect.offsetMin = footerRect.offsetMax = Vector2.zero;
        footer.GetComponent<Image>().color = new Color32(17, 18, 31, 255);
        footer.GetComponent<Image>().raycastTarget = false;
        var caption = new GameObject("ContinueCaption", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        caption.transform.SetParent(footerRect, false);
        comicContinueText = caption.GetComponent<TextMeshProUGUI>();
        comicContinueText.font = MenuButtonStyle.Default != null ? MenuButtonStyle.Default.font : TMP_Settings.defaultFontAsset;
        comicContinueText.fontSize = 32f;
        comicContinueText.fontStyle = FontStyles.Bold;
        comicContinueText.color = new Color32(242, 231, 212, 255);
        comicContinueText.text = "нажмите, чтобы продолжить";
        comicContinueText.alignment = TextAlignmentOptions.Center;
        comicContinueText.raycastTarget = false;
        comicContinueText.rectTransform.anchorMin = Vector2.zero;
        comicContinueText.rectTransform.anchorMax = Vector2.one;
        comicContinueText.rectTransform.offsetMin = new Vector2(12f, 4f);
        comicContinueText.rectTransform.offsetMax = new Vector2(-12f, -4f);
        comicPanel.SetActive(false);
    }

    private void ShowComicPage()
    {
        if (comicPanel == null)
        {
            ShowTutorialPage();
            return;
        }
        comicShowing = true;
        tutorialPanel.SetActive(false);
        comicPanel.SetActive(true);
        comicContinueText.enabled = false;
        comicReadyAt = Time.unscaledTime + Mathf.Max(0f, comicContinueDelay);
        if (overlayImage != null) overlayImage.color = new Color32(10, 11, 20, 255);
    }

    private void Update()
    {
        if (!isShowing || closing || !comicShowing) return;
        bool ready = Time.unscaledTime >= comicReadyAt;
        comicContinueText.enabled = ready;
        if (ready && ContinuePressed()) ShowTutorialPage();
    }

    private static bool ContinuePressed()
    {
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
        Mouse mouse = Mouse.current;
        if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame
            || mouse.middleButton.wasPressedThisFrame || mouse.backButton.wasPressedThisFrame
            || mouse.forwardButton.wasPressedThisFrame)) return true;
        return Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
    }

    private void ShowTutorialPage()
    {
        comicShowing = false;
        if (comicPanel != null) comicPanel.SetActive(false);
        tutorialPanel.SetActive(true);
        tutorialOpenedFrame = Time.frameCount;
        if (overlayImage != null) overlayImage.color = tutorialBackdrop;
    }

    public void StartPlaying()
    {
        if (!isShowing || closing || comicShowing || Time.frameCount == tutorialOpenedFrame)
            return;
        closing = true;
        PlayerPrefs.SetInt(CompletedKey, 1);
        PlayerPrefs.Save();
        AudioManager.Instance?.PlayUiClick();
        StartCoroutine(CloseGuide());
    }

    private IEnumerator CloseGuide()
    {
        overlayFade.SetVisible(false);
        while (overlayFade.gameObject.activeSelf)
            yield return null;
        RestoreGameplay();
    }

    private void RestoreGameplay()
    {
        isShowing = false;
        Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;
        if (gameplayController != null)
            gameplayController.enabled = gameplayWasEnabled;
        if (pauseButton != null)
            pauseButton.interactable = pauseWasInteractable;
    }

    [ContextMenu("Сбросить показ обучения первого уровня")]
    public void ResetGuide()
    {
        PlayerPrefs.DeleteKey(CompletedKey);
        gameplayController?.ResetAbilityHints();
        PlayerPrefs.Save();
    }

    private void OnDestroy()
    {
        if (isShowing)
            RestoreGameplay();
        if (current == this)
            current = null;
    }
}
