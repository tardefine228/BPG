using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>One persistent loading overlay for Boot and all scene changes.</summary>
public sealed class SceneTransition : MonoBehaviour
{
    private static SceneTransition instance;
    private SceneTransitionSettings settings;
    private CanvasGroup group;
    private Image progressFill;
    private TMP_Text caption;
    private TMP_Text percent;
    private bool busy;
    private bool startup;
    public static bool IsLoading => instance != null && instance.busy;
    public static bool StartupVisible => instance != null && instance.startup;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private static SceneTransition GetOrCreate()
    {
        if (instance == null)
            instance = new GameObject("SceneTransition").AddComponent<SceneTransition>();
        return instance;
    }

    public static void Load(string sceneName)
    {
        if (IsLoading)
            return;
        SceneTransition loader = GetOrCreate();
        loader.StartCoroutine(loader.LoadRoutine(sceneName, false));
    }

    public static void ShowStartup(string sceneName)
    {
        if (IsLoading)
            return;
        SceneTransition loader = GetOrCreate();
        loader.StartCoroutine(loader.LoadRoutine(sceneName, true));
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        settings = Resources.Load<SceneTransitionSettings>("SceneTransitionSettings");
        if (settings == null)
            settings = ScriptableObject.CreateInstance<SceneTransitionSettings>();
        BuildInterface();
        group.gameObject.SetActive(false);
    }

    private IEnumerator LoadRoutine(string sceneName, bool isStartup)
    {
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError("Scene is missing from Build Settings: " + sceneName);
            yield break;
        }
        busy = true;
        startup = isStartup;
        Time.timeScale = 1f;
        group.gameObject.SetActive(true);
        group.blocksRaycasts = true;
        group.interactable = false;
        group.alpha = isStartup ? 1f : 0f;
        SetProgress(0f);
        caption.text = isStartup ? settings.startupCaption : settings.loadingCaption;
        if (!isStartup)
            yield return Fade(1f);
        if (isStartup)
            yield return new WaitForSecondsRealtime(settings.startupHold);

        caption.text = settings.loadingCaption;
        float elapsed = 0f;
        float shownProgress = 0f;
        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
        load.allowSceneActivation = false;
        while (load.progress < 0.9f || elapsed < settings.minimumLoadingTime)
        {
            elapsed += Time.unscaledDeltaTime;
            float target = Mathf.Min(load.progress / 0.9f,
                elapsed / Mathf.Max(0.01f, settings.minimumLoadingTime));
            shownProgress = Mathf.MoveTowards(shownProgress, target, Time.unscaledDeltaTime * 2f);
            SetProgress(shownProgress);
            yield return null;
        }
        while (shownProgress < 1f)
        {
            shownProgress = Mathf.MoveTowards(shownProgress, 1f, Time.unscaledDeltaTime * 3f);
            SetProgress(shownProgress);
            yield return null;
        }
        load.allowSceneActivation = true;
        yield return load;
        // Give the destination UI a frame to initialize under the opaque overlay.
        yield return null;
        yield return Fade(0f);
        group.gameObject.SetActive(false);
        startup = false;
        busy = false;
    }

    private IEnumerator Fade(float target)
    {
        float from = group.alpha;
        float elapsed = 0f;
        while (elapsed < settings.fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / settings.fadeDuration));
            group.alpha = Mathf.Lerp(from, target, t);
            yield return null;
        }
        group.alpha = target;
    }

    private void SetProgress(float progress)
    {
        progressFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        percent.text = Mathf.RoundToInt(progress * 100f) + "%";
    }

    private void BuildInterface()
    {
        GameObject canvasObject = new GameObject("LoadingCanvas", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        canvasObject.layer = 5;
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        group = canvasObject.GetComponent<CanvasGroup>();

        RectTransform backdrop = Stretch("Background", canvasObject.transform);
        Image background = backdrop.gameObject.AddComponent<Image>();
        background.color = settings.backgroundColor;
        background.raycastTarget = true;

        RectTransform accent = Center("AccentLine", backdrop, new Vector2(76f, 5f), new Vector2(0f, 260f));
        accent.gameObject.AddComponent<Image>().color = settings.accentColor;

        RectTransform logoRect = Center("LogoPlaceholder", backdrop,
            new Vector2(560f, 210f), new Vector2(0f, 100f));
        Image logo = logoRect.gameObject.AddComponent<Image>();
        logo.sprite = settings.logo;
        logo.preserveAspect = true;
        logo.raycastTarget = false;
        logo.enabled = settings.logo != null;
        Label(backdrop, "GameTitle", settings.title,
            new Vector2(0f, settings.logo != null ? -60f : 100f),
            new Vector2(1100f, 100f), settings.logo != null ? 48f : 74f, settings.textColor);
        Label(backdrop, "StudioText", settings.subtitle, new Vector2(0f, -125f),
            new Vector2(1100f, 60f), 26f, settings.textColor * new Color(1f, 1f, 1f, 0.65f));

        caption = Label(backdrop, "LoadingCaption", settings.loadingCaption, new Vector2(0f, -285f),
            new Vector2(680f, 40f), 22f, settings.textColor);
        RectTransform track = Center("ProgressTrack", backdrop,
            new Vector2(680f, 5f), new Vector2(0f, -330f));
        track.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
        RectTransform fill = Stretch("ProgressFill", track);
        progressFill = fill.gameObject.AddComponent<Image>();
        progressFill.color = settings.accentColor;
        progressFill.raycastTarget = false;
        percent = Label(backdrop, "ProgressPercent", "0%", new Vector2(0f, -375f),
            new Vector2(180f, 40f), 20f, settings.accentColor);
    }

    private static RectTransform Stretch(string name, Transform parent)
    {
        RectTransform rect = Center(name, parent, Vector2.zero, Vector2.zero);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        return rect;
    }

    private static RectTransform Center(string name, Transform parent, Vector2 size, Vector2 position)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.layer = 5;
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private TMP_Text Label(Transform parent, string name, string value, Vector2 position,
        Vector2 size, float fontSize, Color color)
    {
        RectTransform rect = Center(name, parent, size, position);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = MenuButtonStyle.Default.font;
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.raycastTarget = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = fontSize * 0.65f;
        text.fontSizeMax = fontSize;
        return text;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
