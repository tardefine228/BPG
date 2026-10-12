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
    private bool ownsPresentation;

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
    }

    private IEnumerator Start()
    {
        // Сначала показываем чёрный кадр и плавно проявляем компанию.
        // Холодная загрузка меню не должна пропускать кадры появления надписи.
        yield return null;
        yield return Fade(brandingGroup, 1f, settings.fadeInDuration);
        var load = SceneManager.LoadSceneAsync(LevelProgress.MainMenuSceneName);
        load.allowSceneActivation = false;
        yield return new WaitForSecondsRealtime(settings.holdDuration);
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
