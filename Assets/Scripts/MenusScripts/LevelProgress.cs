using UnityEngine;

/// <summary>
/// Persistent level catalog, unlock rules, star records and first-clear scores.
/// Stars are overwritten on every clear. Score is awarded only the first time
/// a level is completed. Level N stays locked until level N-1 has been cleared.
/// </summary>
public static class LevelProgress
{
    public const int MaxStars = 3;
    public const string MainMenuSceneName = "MainMenu";
    public const string GameplaySceneName = "Gameplay";

    private const string StarsKeyPrefix = "bpg_level_stars_";
    private const string CompletedKeyPrefix = "bpg_level_completed_";
    private const string ScoreKeyPrefix = "bpg_level_score_";
    private const string TotalScoreKey = "bpg_total_score";
    private const string CurrentLevelKey = "bpg_current_level_id";

    public static readonly LevelDefinition[] Levels =
    {
        new LevelDefinition(
            1,
            "УРОВЕНЬ 1",
            GameplaySceneName,
            "LevelPreviews/level_1",
            true),
        new LevelDefinition(
            2,
            "УРОВЕНЬ 2",
            string.Empty,
            "LevelPreviews/level_2",
            false),
        new LevelDefinition(
            3,
            "УРОВЕНЬ 3",
            string.Empty,
            "LevelPreviews/level_3",
            false)
    };

    public static int CurrentLevelId { get; private set; } = 1;

    public static LevelDefinition GetLevel(int levelId)
    {
        for (int i = 0; i < Levels.Length; i++)
        {
            if (Levels[i].Id == levelId)
            {
                return Levels[i];
            }
        }

        return Levels[0];
    }

    public static int GetStars(int levelId)
    {
        return Mathf.Clamp(PlayerPrefs.GetInt(StarsKey(levelId), 0), 0, MaxStars);
    }

    public static bool IsCompleted(int levelId)
    {
        return PlayerPrefs.GetInt(CompletedKey(levelId), 0) == 1
            || GetStars(levelId) > 0;
    }

    public static bool IsUnlocked(int levelId)
    {
        if (levelId <= 1)
        {
            return true;
        }

        return IsCompleted(levelId - 1);
    }

    public static bool HasAwardedScore(int levelId)
    {
        return PlayerPrefs.GetInt(CompletedKey(levelId), 0) == 1
            || GetLevelScore(levelId) > 0;
    }

    public static int GetLevelScore(int levelId)
    {
        return Mathf.Max(0, PlayerPrefs.GetInt(ScoreKey(levelId), 0));
    }

    public static int GetTotalScore()
    {
        return Mathf.Max(0, PlayerPrefs.GetInt(TotalScoreKey, 0));
    }

    public static void SelectLevel(int levelId)
    {
        CurrentLevelId = Mathf.Max(1, levelId);
        PlayerPrefs.SetInt(CurrentLevelKey, CurrentLevelId);
        PlayerPrefs.Save();
    }

    public static void RestoreSelectedLevel()
    {
        CurrentLevelId = Mathf.Max(1, PlayerPrefs.GetInt(CurrentLevelKey, 1));
    }

    public static void ResetStarsForRun(int levelId)
    {
        PlayerPrefs.SetInt(StarsKey(levelId), 0);
        PlayerPrefs.Save();
    }

    public static void SaveStars(int levelId, int stars)
    {
        int clamped = Mathf.Clamp(stars, 0, MaxStars);
        PlayerPrefs.SetInt(StarsKey(levelId), clamped);
        PlayerPrefs.Save();
    }

    public static void MarkCompleted(int levelId)
    {
        PlayerPrefs.SetInt(CompletedKey(levelId), 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Stores the run score only if this is the first time the level is cleared.
    /// Returns false when the score is ignored on a replay.
    /// </summary>
    public static bool TryAwardFirstClearScore(int levelId, int score)
    {
        if (HasAwardedScore(levelId))
        {
            return false;
        }

        int awarded = Mathf.Max(0, score);
        PlayerPrefs.SetInt(ScoreKey(levelId), awarded);
        PlayerPrefs.SetInt(TotalScoreKey, GetTotalScore() + awarded);
        PlayerPrefs.Save();
        return true;
    }

    public static int StarsFromShots(int shotsFired)
    {
        if (shotsFired <= 1)
        {
            return 3;
        }

        if (shotsFired <= 2)
        {
            return 2;
        }

        return 1;
    }

    public static int ComputeRunScore(int clearedBoxes, int shotsFired, float totalTravelDistance)
    {
        int boxScore = Mathf.Max(0, clearedBoxes) * 1000;
        int distanceScore = Mathf.RoundToInt(Mathf.Max(0f, totalTravelDistance) * 140f);
        int shotBonus = shotsFired <= 0
            ? 0
            : Mathf.Max(0, 1500 - (shotsFired - 1) * 400);
        return boxScore + distanceScore + shotBonus;
    }

    private static string StarsKey(int levelId)
    {
        return StarsKeyPrefix + levelId;
    }

    private static string CompletedKey(int levelId)
    {
        return CompletedKeyPrefix + levelId;
    }

    private static string ScoreKey(int levelId)
    {
        return ScoreKeyPrefix + levelId;
    }
}

public readonly struct LevelDefinition
{
    public readonly int Id;
    public readonly string DisplayName;
    public readonly string SceneName;
    public readonly string PreviewResourcePath;
    public readonly bool IsImplemented;

    public LevelDefinition(
        int id,
        string displayName,
        string sceneName,
        string previewResourcePath,
        bool isImplemented)
    {
        Id = id;
        DisplayName = displayName;
        SceneName = sceneName;
        PreviewResourcePath = previewResourcePath;
        IsImplemented = isImplemented;
    }

    public bool CanLoad =>
        IsImplemented && !string.IsNullOrEmpty(SceneName);
}
