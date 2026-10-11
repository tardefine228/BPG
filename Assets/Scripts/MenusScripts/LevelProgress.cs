using UnityEngine;

// Каталог уровней и сохранение прогресса: звёзды обновляются при прохождении, очки начисляются один раз, следующий уровень открывается после предыдущего.
public static class LevelProgress
{
    public const int MaxStars = 3;

    // Очки за каждую побеждённую мышь — главную цель уровня.
    public const int MouseScore = 5000;
    public const string MainMenuSceneName = "MainMenu";
    public const string GameplaySceneName = "Gameplay";
    public const string SecondLevelSceneName = "Level2";
    public const string ThirdLevelSceneName = "Level3";
    public const string FourthLevelSceneName = "Level4";
    public const string FifthLevelSceneName = "Level5";

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
            true,
            1,
            2),
        new LevelDefinition(
            2,
            "УРОВЕНЬ 2",
            SecondLevelSceneName,
            "LevelPreviews/level_2",
            true,
            2,
            3),
        new LevelDefinition(
            3,
            "УРОВЕНЬ 3",
            ThirdLevelSceneName,
            "LevelPreviews/level_3",
            true,
            2,
            3),
        new LevelDefinition(
            4,
            "УРОВЕНЬ 4",
            FourthLevelSceneName,
            "LevelPreviews/level_4",
            true,
            2,
            4),
        new LevelDefinition(
            5,
            "УРОВЕНЬ 5",
            FifthLevelSceneName,
            "LevelPreviews/level_5",
            true,
            3,
            4)
    };

    public static int CurrentLevelId { get; private set; } = 1;

    // Режим тестирования позволяет открывать все уровни без последовательного прохождения.
    public static bool UnlockAllLevels { get; set; }

    public static bool IsGameplayScene(string sceneName)
    {
        foreach (LevelDefinition level in Levels)
            if (level.CanLoad && level.SceneName == sceneName)
                return true;
        return false;
    }

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
        if (UnlockAllLevels || levelId <= 1)
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

    // Сохраняет очки только за первое прохождение; повторное не приносит новую награду.
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

    // Количество звёзд зависит от числа выстрелов и порогов уровня.
    public static int StarsForShots(int levelId, int shotsFired)
    {
        LevelDefinition level = GetLevel(levelId);
        if (shotsFired <= 0)
        {
            shotsFired = 1;
        }

        if (shotsFired <= level.ThreeStarShots)
        {
            return 3;
        }

        if (shotsFired <= level.TwoStarShots)
        {
            return 2;
        }

        return 1;
    }

    // Порог выстрелов для трёх звёзд.
    public static int ThreeStarShots(int levelId)
    {
        return Mathf.Max(1, GetLevel(levelId).ThreeStarShots);
    }

    // Общее правило оценки для уровней без собственных порогов.
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

    // Очки попытки: сбитые блоки, побеждённые мыши, разлёт блоков и бонус за экономию выстрелов.
    public static int ComputeRunScore(int clearedBoxes, int defeatedMice, int shotsFired, float totalTravelDistance)
    {
        int boxScore = Mathf.Max(0, clearedBoxes) * 1000;
        int mouseScore = Mathf.Max(0, defeatedMice) * MouseScore;
        int distanceScore = Mathf.RoundToInt(Mathf.Max(0f, totalTravelDistance) * 140f);
        int shotBonus = shotsFired <= 0
            ? 0
            : Mathf.Max(0, 1500 - (shotsFired - 1) * 400);
        return boxScore + mouseScore + distanceScore + shotBonus;
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

    // Максимум выстрелов для трёх звёзд.
    public readonly int ThreeStarShots;

    // Максимум выстрелов для двух звёзд.
    public readonly int TwoStarShots;

    public LevelDefinition(
        int id,
        string displayName,
        string sceneName,
        string previewResourcePath,
        bool isImplemented,
        int threeStarShots = 1,
        int twoStarShots = 2)
    {
        Id = id;
        DisplayName = displayName;
        SceneName = sceneName;
        PreviewResourcePath = previewResourcePath;
        IsImplemented = isImplemented;
        ThreeStarShots = Mathf.Max(1, threeStarShots);
        TwoStarShots = Mathf.Max(ThreeStarShots, twoStarShots);
    }

    public bool CanLoad =>
        IsImplemented && !string.IsNullOrEmpty(SceneName);
}
