using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// В каждой сцене есть готовый экран загрузки. Состояние передаётся следующей сцене.
public sealed class SceneTransition : MonoBehaviour
{
    private static SceneTransition instance;
    private static bool busy;
    private static bool startup;
    private bool arriving;
    [SerializeField] private SceneTransitionSettings settings;
    [SerializeField] private CanvasGroup group;
    [SerializeField] private Image progressFill;
    [SerializeField] private TMP_Text caption;
    [SerializeField] private TMP_Text percent;
    [SerializeField] private Image logoImage;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text subtitleText;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image accentLine;
    public static bool IsLoading => busy;
    public static bool StartupVisible => startup;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        busy = startup = false;
    }

    public static void Load(string sceneName) => BeginLoad(sceneName, false);
    public static void ShowStartup(string sceneName) => BeginLoad(sceneName, true);

    private static void BeginLoad(string sceneName, bool isStartup)
    {
        if (busy) return;
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError("Сцена отсутствует в списке сборки: " + sceneName);
            return;
        }
        if (instance == null)
            instance = FindAnyObjectByType<SceneTransition>(FindObjectsInactive.Include);
        if (instance == null)
        {
            Debug.LogError("В сцене отсутствует настроенный SceneTransition.");
            return;
        }
        instance.StartCoroutine(instance.LoadRoutine(sceneName, isStartup));
    }

    private void Awake()
    {
        arriving = busy;
        instance = this;
        if (settings == null)
            settings = Resources.Load<SceneTransitionSettings>("SceneTransitionSettings");
        RefreshContent();
        group.gameObject.SetActive(busy);
        group.alpha = busy ? 1f : 0f;
        group.interactable = false;
        group.blocksRaycasts = busy;
    }

    private IEnumerator Start()
    {
        if (!arriving) yield break;
        caption.text = settings.loadingCaption;
        SetProgress(1f);
        // Новая сцена успевает подготовить интерфейс под непрозрачным экраном.
        yield return null;
        yield return Fade(0f);
        group.gameObject.SetActive(false);
        busy = startup = false;
    }

    private IEnumerator LoadRoutine(string sceneName, bool isStartup)
    {
        busy = true;
        startup = isStartup;
        Time.timeScale = 1f;
        group.gameObject.SetActive(true);
        group.blocksRaycasts = true;
        group.alpha = isStartup ? 1f : 0f;
        SetProgress(0f);
        caption.text = isStartup ? settings.startupCaption : settings.loadingCaption;
        if (!isStartup) yield return Fade(1f);
        if (isStartup) yield return new WaitForSecondsRealtime(settings.startupHold);
        caption.text = settings.loadingCaption;

        float elapsed = 0f;
        float shown = 0f;
        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
        load.allowSceneActivation = false;
        while (load.progress < 0.9f || elapsed < settings.minimumLoadingTime)
        {
            elapsed += Time.unscaledDeltaTime;
            float target = Mathf.Min(load.progress / 0.9f,
                elapsed / Mathf.Max(0.01f, settings.minimumLoadingTime));
            shown = Mathf.MoveTowards(shown, target, Time.unscaledDeltaTime * 2f);
            SetProgress(shown);
            yield return null;
        }
        while (shown < 1f)
        {
            shown = Mathf.MoveTowards(shown, 1f, Time.unscaledDeltaTime * 3f);
            SetProgress(shown);
            yield return null;
        }
        // После активации эта сцена выгружается. Завершает переход её новый SceneTransition.
        load.allowSceneActivation = true;
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

    private void RefreshContent()
    {
        titleText.text = settings.title;
        subtitleText.text = settings.subtitle;
        logoImage.sprite = settings.logo;
        logoImage.enabled = settings.logo != null;
    }

    // Явное применение палитры в редакторе сохраняет возможность оформлять prefab вручную.
    [ContextMenu("Применить оформление из SceneTransitionSettings")]
    public void ApplyAppearanceSettings()
    {
        RefreshContent();
        backgroundImage.color = settings.backgroundColor;
        accentLine.color = progressFill.color = percent.color = settings.accentColor;
        titleText.color = caption.color = settings.textColor;
        subtitleText.color = settings.textColor * new Color(1f, 1f, 1f, 0.65f);
        Vector2 position = titleText.rectTransform.anchoredPosition;
        position.y = settings.logo != null ? -60f : 100f;
        titleText.rectTransform.anchoredPosition = position;
        titleText.fontSize = titleText.fontSizeMax = settings.logo != null ? 48f : 74f;
        titleText.fontSizeMin = titleText.fontSizeMax * 0.65f;
    }
}
