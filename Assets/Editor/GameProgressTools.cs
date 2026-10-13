using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Editor-only preparation for a fresh playthrough. Preferences never live in the repository.
[InitializeOnLoad]
public static class GameProgressTools
{
    private const string ResetMenu = "BPG/Сбросить прогресс и обучение";
    private static readonly string RequestPath = Path.GetFullPath(
        Path.Combine(Application.dataPath, "../Temp/BPG-reset-progress.once"));
    private static readonly string ReportPath = Path.GetFullPath(
        Path.Combine(Application.dataPath, "../Temp/BPG-progress-reset.done"));

    static GameProgressTools()
    {
        EditorApplication.update += ConsumeResetRequest;
    }

    [MenuItem(ResetMenu)]
    public static void ResetProgress()
    {
        if (!CanResetProgress())
            throw new InvalidOperationException("Stop Play Mode before resetting progress.");

        var keys = new List<string>
        {
            "bpg_total_score", "bpg_current_level_id",
            "BPG.Guide.Gameplay.v1", "BPG.Guide.AbilityUsed.v2", "BPG.Guide.AbilityShots.v1"
        };
        foreach (LevelDefinition level in LevelProgress.Levels)
        {
            keys.Add("bpg_level_stars_" + level.Id);
            keys.Add("bpg_level_completed_" + level.Id);
            keys.Add("bpg_level_score_" + level.Id);
        }
        foreach (string key in keys)
            PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
        LevelProgress.UnlockAllLevels = false;
        LevelProgress.RestoreSelectedLevel();

        foreach (string key in keys)
            if (PlayerPrefs.HasKey(key))
                throw new InvalidOperationException("Preference was not cleared: " + key);
        foreach (LevelDefinition level in LevelProgress.Levels)
            if (LevelProgress.GetStars(level.Id) != 0 || LevelProgress.IsCompleted(level.Id)
                || LevelProgress.GetLevelScore(level.Id) != 0
                || LevelProgress.IsUnlocked(level.Id) != (level.Id == LevelProgress.Levels[0].Id))
                throw new InvalidOperationException("Initial state was not restored for level " + level.Id);
        if (LevelProgress.GetTotalScore() != 0 || LevelProgress.CurrentLevelId != 1
            || FirstLevelGuide.HasBeenCompleted)
            throw new InvalidOperationException("Initial progress/tutorial state was not restored.");

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, "PASS: " + keys.Count + " progress/tutorial keys absent; "
            + "stars=0; score=0; selectedLevel=1; onlyFirstLevelUnlocked=true; tutorialCompleted=false; "
            + "utc=" + DateTime.UtcNow.ToString("O"));
        if (File.Exists(RequestPath))
            File.Delete(RequestPath);
        Debug.Log("BPG: прогресс и обучение сброшены; открыт первый уровень. Настройки звука сохранены.");
    }

    [MenuItem(ResetMenu, true)]
    private static bool CanResetProgress()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling
            && !EditorApplication.isUpdating;
    }

    private static void ConsumeResetRequest()
    {
        if (!File.Exists(RequestPath))
        {
            EditorApplication.update -= ConsumeResetRequest;
            return;
        }
        if (!CanResetProgress())
            return;
        EditorApplication.update -= ConsumeResetRequest;
        ResetProgress();
    }
}
