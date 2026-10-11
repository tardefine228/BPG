using UnityEngine;

// В Boot нет AudioManager; музыка меню начинается после исчезновения заставки.
public sealed class StartupController : MonoBehaviour
{
    private void Start()
    {
        SceneTransition.ShowStartup(LevelProgress.MainMenuSceneName);
    }
}
