using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class MenuManager : MonoBehaviour
{
    [SerializeField] private CanvasGroup mainMenuGroup;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private CanvasGroup settingsGroup;

    [SerializeField] private float fadeDuration = 0.15f;

    private bool settingsOpen;
    private Coroutine fadeRoutine;

    private void Start()
    {
        settingsPanel.SetActive(false);
        settingsGroup.alpha = 0f;

        #if UNITY_ANDROID
                Application.wantsMobileBackButton = true;
        #endif
    }

    public void OpenSettings()
    {
        if (settingsOpen)
        {
            return;
        }

        settingsOpen = true;

        SetMainMenuEnabled(false);

        settingsPanel.SetActive(true);
        settingsGroup.blocksRaycasts = true;
        settingsGroup.interactable = true;

        RestartFade(1f, null);
    }

    public void CloseSettings()
    {
        if (!settingsOpen)
        {
            return;
        }

        settingsOpen = false;

        settingsGroup.interactable = false;

        RestartFade(0f, () =>
        {
            settingsPanel.SetActive(false);
            SetMainMenuEnabled(true);
        });
    }

    private void Update()
    {
        #if ENABLE_INPUT_SYSTEM
                bool escapePressed = Keyboard.current != null
                    && Keyboard.current.escapeKey.wasPressedThisFrame;
        #else
                bool escapePressed = Input.GetKeyDown(KeyCode.Escape);
        #endif

        if (escapePressed && settingsOpen)
        {
            AudioManager.Instance?.PlayUiClose();
            CloseSettings();
        }
    }

    private void SetMainMenuEnabled(bool enabled)
    {
        mainMenuGroup.interactable = enabled;
        mainMenuGroup.blocksRaycasts = enabled;
    }

    private void RestartFade(float targetAlpha, Action onComplete)
    {
        StopFade();
        fadeRoutine = StartCoroutine(FadeRoutine(targetAlpha, onComplete));
    }

    private void StopFade()
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }
    }

    private IEnumerator FadeRoutine(float targetAlpha, Action onComplete)
    {
        float startAlpha = settingsGroup.alpha;
        float time = 0f;

        while (time < fadeDuration)
        {
            time += Time.unscaledDeltaTime;
            settingsGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, time / fadeDuration);
            yield return null;
        }

        settingsGroup.alpha = targetAlpha;
        onComplete?.Invoke();
    }
}
