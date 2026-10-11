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
    [SerializeField] private Image backgroundImage;
    public static bool IsLoading => busy;
    public static bool StartupVisible => startup;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        busy = startup = false;
    }

    public static void Load(string sceneName) => BeginLoad(sceneName);

    // Boot показывает собственную заставку и сам раскрывает меню из чёрного.
    public static void BeginStartupPresentation()
    {
        busy = startup = true;
        Time.timeScale = 1f;
    }

    public static void FinishStartupPresentation() => busy = startup = false;

    private static void BeginLoad(string sceneName)
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
        instance.StartCoroutine(instance.LoadRoutine(sceneName));
    }

    private void Awake()
    {
        arriving = busy && !startup;
        instance = this;
        if (settings == null)
            settings = Resources.Load<SceneTransitionSettings>("SceneTransitionSettings");
        RefreshContent();
        group.gameObject.SetActive(arriving);
        group.alpha = arriving ? 1f : 0f;
        group.interactable = false;
        group.blocksRaycasts = arriving;
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

    private IEnumerator LoadRoutine(string sceneName)
    {
        busy = true;
        startup = false;
        Time.timeScale = 1f;
        group.gameObject.SetActive(true);
        group.blocksRaycasts = true;
        group.alpha = 0f;
        SetProgress(0f);
        caption.text = settings.loadingCaption;
        yield return Fade(1f);

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
        caption.text = settings.loadingCaption;
    }

    // Явное применение палитры в редакторе сохраняет возможность оформлять prefab вручную.
    [ContextMenu("Применить оформление из SceneTransitionSettings")]
    public void ApplyAppearanceSettings()
    {
        RefreshContent();
        backgroundImage.color = settings.backgroundColor;
        progressFill.color = percent.color = settings.accentColor;
        caption.color = settings.textColor;
    }
}
