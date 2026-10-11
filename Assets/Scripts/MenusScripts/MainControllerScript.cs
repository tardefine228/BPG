using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Main menu buttons: play, settings and quit.
/// </summary>
public class MainControllerScript : MonoBehaviour
{
    [SerializeField] private Button playButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private MenuManager menuManager;

    private void Awake()
    {
        if (menuManager == null)
            menuManager = FindAnyObjectByType<MenuManager>();
        // The scene normally carries the reference; the lookup keeps the button
        // working if the component is re-created or the reference is lost.
        if (quitButton == null)
        {
            quitButton = FindQuitButton();
        }

        AddListener(playButton, OnPlay);
        AddListener(settingsButton, OnSettings);
        AddListener(quitButton, OnQuit);

#if UNITY_WEBGL && !UNITY_EDITOR
        // A browser page is not allowed to close its own tab, so the button
        // would only confuse the player there.
        if (quitButton != null)
        {
            quitButton.gameObject.SetActive(false);
        }
#endif
    }

    private void OnDestroy()
    {
        RemoveListener(playButton, OnPlay);
        RemoveListener(settingsButton, OnSettings);
        RemoveListener(quitButton, OnQuit);
    }

    private static void AddListener(Button button, UnityAction action)
    {
        if (button != null)
        {
            button.onClick.AddListener(action);
        }
    }

    private static void RemoveListener(Button button, UnityAction action)
    {
        if (button != null)
        {
            button.onClick.RemoveListener(action);
        }
    }

    private void OnPlay()
    {
        AudioManager.Instance?.PlayUiClick();
        menuManager.OpenLevels();
    }

    private void OnSettings()
    {

    }

    private void OnQuit()
    {
        AudioManager.Instance?.PlayUiClick();
        if (quitButton != null)
        {
            quitButton.interactable = false;
        }

        QuitGame();
    }

    /// <summary>
    /// Ends the game. In a built player <c>Application.Quit</c> closes the
    /// process, but in the Editor it does nothing — there play mode is stopped
    /// instead, so the button also works while testing the game in Unity.
    /// </summary>
    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static Button FindQuitButton()
    {
        GameObject found = GameObject.Find("ExitButton");
        return found != null ? found.GetComponent<Button>() : null;
    }
}
