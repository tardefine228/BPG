using System.Collections;
using UnityEngine;
using UnityEngine.UI;

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
    private bool isShowing;
    private bool closing;
    private bool gameplayWasEnabled;
    private bool pauseWasInteractable;
    private float previousTimeScale;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => current = null;

    private void Awake()
    {
        current = this;
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
        overlayFade.SetVisible(true);
    }

    public void StartPlaying()
    {
        if (!isShowing || closing)
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
