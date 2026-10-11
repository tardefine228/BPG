using UnityEngine;
using UnityEngine.UI;

// Действия главного меню назначены в On Click у готовых кнопок сцены.
public sealed class MainControllerScript : MonoBehaviour
{
    [SerializeField] private Button quitButton;
    [SerializeField] private MenuManager menuManager;

    private void Awake()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // Браузер запрещает странице закрывать собственную вкладку.
        quitButton.gameObject.SetActive(false);
#endif
    }

    public void OnPlay()
    {
        AudioManager.Instance?.PlayUiClick();
        menuManager.OpenLevels();
    }

    public void OnSettings()
    {
        AudioManager.Instance?.PlayUiClick();
        menuManager.OpenSettings();
    }

    public void OnQuit()
    {
        AudioManager.Instance?.PlayUiClick();
        quitButton.interactable = false;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
