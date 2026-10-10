using UnityEngine;

/// <summary>
/// Persistent level catalog, unlock rules and star records.
/// Level N is locked until level N-1 has been completed with at least one star.
/// </summary>
public static class LevelProgress
{
    public const int MaxStars = 3;
    public const string LobbySceneName = "LevelSelect";
    public const string GameplaySceneName = "Gameplay";

    private const string StarsKeyPrefix = "bpg_level_stars_";
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
        return GetStars(levelId) > 0;
    }

    public static bool IsUnlocked(int levelId)
    {
        if (levelId <= 1)
        {
            return true;
        }

        return IsCompleted(levelId - 1);
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

    public static void SaveStars(int levelId, int stars)
    {
        int clamped = Mathf.Clamp(stars, 0, MaxStars);
        int previous = GetStars(levelId);
        if (clamped <= previous)
        {
            return;
        }

        PlayerPrefs.SetInt(StarsKey(levelId), clamped);
        PlayerPrefs.Save();
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

    private static string StarsKey(int levelId)
    {
        return StarsKeyPrefix + levelId;
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
