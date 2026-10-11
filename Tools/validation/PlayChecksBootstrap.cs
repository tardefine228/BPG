using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class PlayChecksBootstrap
{
    static PlayChecksBootstrap() { EditorApplication.playModeStateChanged += Changed; }
    public static void Run()
    {
        if (PlayerSettings.companyName != "BPGChecks") throw new System.Exception("Run only in the isolated project");
        PlayerPrefs.DeleteAll();
        SceneUiValidation.Validate();
        EditorSceneManager.OpenScene("Assets/Scenes/Boot.unity");
        SessionState.SetBool("BPGChecksRunning", true);
        EditorApplication.isPlaying = true;
    }
    private static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("BPGChecksRunning", false)) return;
        SessionState.SetBool("BPGChecksRunning", false);
        new GameObject("ValidationRunner").AddComponent<ValidationRunner>();
    }
}
