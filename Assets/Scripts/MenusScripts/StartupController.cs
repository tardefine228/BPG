using UnityEngine;

/// <summary>Boot has no AudioManager. Main-menu music starts after the splash fades away.</summary>
public sealed class StartupController : MonoBehaviour
{
    private void Start()
    {
        SceneTransition.ShowStartup(LevelProgress.MainMenuSceneName);
    }
}
