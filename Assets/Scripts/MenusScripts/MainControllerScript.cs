using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

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
        AudioManager.Instance?.PlayUiClick();
        menuManager.OpenLevels();
    }

    private void OnSettings()
    {

    }

    private void OnQuit()
    {
        Application.Quit();
    }
}
