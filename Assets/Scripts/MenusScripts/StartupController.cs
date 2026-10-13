using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// В Boot нет AudioManager; музыка меню начинается после исчезновения заставки.
public sealed class StartupController : MonoBehaviour
{
    [SerializeField] private StartupSplashSettings settings;
    [SerializeField] private CanvasGroup screenGroup;
    [SerializeField] private CanvasGroup brandingGroup;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image logoImage;
    [SerializeField] private TMP_Text companyNameText;
    [SerializeField] private Image runningCatImage;
    private bool ownsPresentation;
    private Vector2 largestCatFrame;
    private float lowestCatFrame;
    private bool hasRunningCat;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        SceneTransition.BeginStartupPresentation();
        ownsPresentation = true;
        screenGroup.alpha = 1f;
        screenGroup.blocksRaycasts = true;
        brandingGroup.alpha = 0f;
        backgroundImage.color = settings.backgroundColor;
        logoImage.sprite = settings.logo;
        logoImage.enabled = settings.logo != null;
        companyNameText.text = settings.companyName;
        companyNameText.color = settings.textColor;
        PrepareRunningCat();
    }

    private IEnumerator Start()
    {
        // Сначала показываем чёрный кадр и плавно проявляем компанию.
        // Холодная загрузка меню не должна пропускать кадры появления надписи.
        yield return null;
        yield return Fade(brandingGroup, 1f, settings.fadeInDuration);
        var load = SceneManager.LoadSceneAsync(LevelProgress.MainMenuSceneName);
        load.allowSceneActivation = false;
        yield return HoldBranding();
        yield return Fade(brandingGroup, 0f, settings.fadeOutDuration);
        while (load.progress < 0.9f)
            yield return null;
        load.allowSceneActivation = true;
        while (!load.isDone)
            yield return null;
        yield return null;
        yield return Fade(screenGroup, 0f, settings.menuRevealDuration);
        SceneTransition.FinishStartupPresentation();
        ownsPresentation = false;
        Destroy(gameObject);
    }

    private void PrepareRunningCat()
    {
        if (runningCatImage == null) return;
        runningCatImage.enabled = false;
        runningCatImage.raycastTarget = false;
        if (!settings.showRunningCat || settings.runningCatFrames == null) return;
        lowestCatFrame = float.PositiveInfinity;
        foreach (Sprite frame in settings.runningCatFrames)
        {
            if (frame == null) continue;
            hasRunningCat = true;
            largestCatFrame = Vector2.Max(largestCatFrame, frame.rect.size);
            lowestCatFrame = Mathf.Min(lowestCatFrame, frame.rect.yMin);
        }
        runningCatImage.rectTransform.anchorMin = runningCatImage.rectTransform.anchorMax = Vector2.one * 0.5f;
        runningCatImage.rectTransform.pivot = new Vector2(0.5f, 0f);
    }

    private IEnumerator HoldBranding()
    {
        float duration = Mathf.Max(0.1f, settings.runningCatDuration);
        float delay = Mathf.Max(0f, settings.runningCatDelay);
        // Даже при коротком Hold Duration кот успевает выйти за край экрана до затемнения.
        float hold = hasRunningCat ? Mathf.Max(settings.holdDuration, delay + duration + 0.15f)
            : Mathf.Max(0f, settings.holdDuration);
        double started = Time.realtimeSinceStartupAsDouble;
        while (Time.realtimeSinceStartupAsDouble - started < hold)
        {
            if (hasRunningCat)
                UpdateRunningCat((float)(Time.realtimeSinceStartupAsDouble - started) - delay, duration);
            yield return null;
        }
        if (runningCatImage != null) runningCatImage.enabled = false;
    }

    private void UpdateRunningCat(float elapsed, float duration)
    {
        runningCatImage.enabled = elapsed >= 0f && elapsed < duration;
        if (!runningCatImage.enabled) return;
        int index = Mathf.FloorToInt(elapsed * Mathf.Max(1f, settings.runningCatFramesPerSecond))
            % settings.runningCatFrames.Length;
        Sprite frame = settings.runningCatFrames[index];
        if (frame == null) { runningCatImage.enabled = false; return; }
        RectTransform rect = runningCatImage.rectTransform;
        RectTransform parent = (RectTransform)rect.parent;
        RectTransform title = companyNameText.rectTransform;
        float scale = Mathf.Max(1f, settings.runningCatPixelScale);
        float halfRunWidth = parent.rect.width * 0.5f + largestCatFrame.x * scale * 0.5f + 24f;
        float titleBottom = parent.InverseTransformPoint(title.TransformPoint(new Vector3(0f, title.rect.yMin, 0f))).y;
        float baseline = titleBottom - settings.runningCatTextGap - largestCatFrame.y * scale;
        runningCatImage.sprite = frame;
        // Разная обрезка кадров сохраняет их пропорции, масштаб пикселя и высоту относительно земли.
        rect.sizeDelta = frame.rect.size * scale;
        rect.anchoredPosition = new Vector2(Mathf.Round(Mathf.Lerp(-halfRunWidth, halfRunWidth, elapsed / duration)),
            Mathf.Round(baseline + (frame.rect.yMin - lowestCatFrame) * scale));
    }

    private static IEnumerator Fade(CanvasGroup group, float target, float duration)
    {
        float from = group.alpha;
        double started = Time.realtimeSinceStartupAsDouble;
        while (Time.realtimeSinceStartupAsDouble - started < duration)
        {
            float elapsed = (float)(Time.realtimeSinceStartupAsDouble - started);
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            group.alpha = Mathf.Lerp(from, target, t);
            yield return null;
        }
        group.alpha = target;
    }

    private void OnDestroy()
    {
        if (ownsPresentation)
            SceneTransition.FinishStartupPresentation();
    }
}
