using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainControllerScript : MonoBehaviour
{

    [SerializeField] private Button playButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;

    private void Awake()
    {
        AddListener(playButton, OnPlay);
        AddListener(settingsButton, OnSettings);
        AddListener(quitButton, OnQuit);
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
        if (playButton != null)
        {
            playButton.interactable = false;
        }

        SceneManager.LoadScene(LevelProgress.LobbySceneName);
    }

    private void OnSettings()
    {

    }

    private void OnQuit()
    {
        Application.Quit();
    }
}
