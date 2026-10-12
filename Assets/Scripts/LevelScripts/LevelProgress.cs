using UnityEngine;

// Каталог уровней и сохранение прогресса: звёзды обновляются при прохождении, очки начисляются один раз.
// Второй уровень открывается после первого, а начиная с третьего доступ даёт общее количество собранных звёзд.
public static class LevelProgress
{
    public const int MaxStars = 3;
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

    // Поля каждой строки: номер, надпись на карточке, короткое название уровня,
    // сцена, картинка-превью, готовность, порог звёзд и резервные пороги выстрелов.
    // Короткое название видно под звёздами карточки; порог звёзд 0 означает,
    // что уровень открывается после предыдущего, а 5, 8 и 12 звёзд нужны
    // для третьего, четвёртого и пятого.
    public static readonly LevelDefinition[] Levels =
    {
        new LevelDefinition(
            1,
            "УРОВЕНЬ 1",
            "СОЛНЕЧНЫЙ ДВОР",
            GameplaySceneName,
            "LevelPreviews/level_1",
            true,
            0,
            2,
            3),
        new LevelDefinition(
            2,
            "УРОВЕНЬ 2",
            "АЛЫЙ ЗАКАТ",
            SecondLevelSceneName,
            "LevelPreviews/level_2",
            true,
            0,
            3,
            9),
        new LevelDefinition(
            3,
            "УРОВЕНЬ 3",
            "ЗВЁЗДНАЯ НОЧЬ",
            ThirdLevelSceneName,
            "LevelPreviews/level_3",
            true,
            5,
            4,
            16),
        new LevelDefinition(
            4,
            "УРОВЕНЬ 4",
            "ПЕСЧАНЫЙ КАНЬОН",
            FourthLevelSceneName,
            "LevelPreviews/level_4",
            true,
            8,
            5,
            17),
        new LevelDefinition(
            5,
            "УРОВЕНЬ 5",
            "МЫШИНАЯ КРЕПОСТЬ",
            FifthLevelSceneName,
            "LevelPreviews/level_5",
            true,
            12,
            5,
            19)
    };

    public static int CurrentLevelId { get; private set; } = 1;

    // Режим тестирования позволяет открывать все уровни без последовательного прохождения.
    public static bool UnlockAllLevels { get; set; }

    // Сумма всех собранных звёзд — ею открываются третий, четвёртый и пятый уровни.
    public static int TotalStars => CountStars(Levels);

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
        if (UnlockAllLevels)
        {
            return true;
        }

        return IsUnlockedByRules(levelId, TotalStars, IsCompleted(levelId - 1));
    }

    // Чистое правило доступа: без режима тестирования и без чтения сохранений.
    // Так порог звёзд можно проверять отдельно от PlayerPrefs.
    public static bool IsUnlockedByRules(int levelId, int totalStars, bool previousLevelCompleted)
    {
        if (levelId <= 1)
        {
            return true;
        }

        int requiredStars = RequiredStarsFor(levelId);
        if (requiredStars > 0)
        {
            return totalStars >= requiredStars;
        }

        return previousLevelCompleted;
    }

    // Сколько звёзд нужно для уровня. Ноль — уровень открывается за предыдущий.
    public static int RequiredStarsFor(int levelId)
    {
        return GetLevel(levelId).RequiredStars;
    }

    // Сколько звёзд ещё не хватает до открытия; у открытого уровня ноль.
    public static int StarsMissingFor(int levelId)
    {
        if (IsUnlocked(levelId))
        {
            return 0;
        }

        return Mathf.Max(0, RequiredStarsFor(levelId) - TotalStars);
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
        // Звёзды — лучший результат уровня: не теряем уже открытые награды
        // при менее удачном повторном прохождении.
        int best = Mathf.Max(GetStars(levelId), Mathf.Clamp(stars, 0, MaxStars));
        PlayerPrefs.SetInt(StarsKey(levelId), best);
        PlayerPrefs.Save();
    }

    // Переводит долю убитых обязательных мышей в 1–3 звезды.
    // Полный зачёт всегда требует всех мышей; при любом неполном зачёте
    // результат ограничен двумя звёздами. Одно убийство гарантирует минимум
    // одну звезду, чтобы небольшое число мышей на уровне не округлилось в ноль.
    public static int StarsForDefeatedMice(int defeatedMice, int totalMice)
    {
        if (totalMice <= 0)
            return 0;

        int defeated = Mathf.Clamp(defeatedMice, 0, totalMice);
        if (defeated == 0)
            return 0;
        if (defeated == totalMice)
            return MaxStars;

        int roundedStars = Mathf.RoundToInt((float)defeated / totalMice * MaxStars);
        return Mathf.Clamp(roundedStars, 1, MaxStars - 1);
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

    // Резервная оценка по выстрелам для уровней без обязательных мышей.
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

    // Порог выстрелов для резервной оценки уровней без обязательных мышей.
    public static int ThreeStarShots(int levelId)
    {
        return Mathf.Max(1, GetLevel(levelId).ThreeStarShots);
    }

    // Резервное общее правило для уровней без обязательных мышей и собственных порогов.
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

    private static int CountStars(LevelDefinition[] levels)
    {
        int total = 0;
        for (int i = 0; i < levels.Length; i++)
        {
            total += GetStars(levels[i].Id);
        }

        return total;
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

    // Короткое название уровня: «СОЛНЕЧНЫЙ ДВОР», «ЗВЁЗДНАЯ НОЧЬ». Видно под
    // звёздами карточки в меню выбора уровня.
    public readonly string Tagline;
    public readonly string SceneName;
    public readonly string PreviewResourcePath;
    public readonly bool IsImplemented;

    // Порог звёзд для открытия уровня. Ноль — уровень открывается после предыдущего.
    public readonly int RequiredStars;

    // Резервные пороги выстрелов для уровней без обязательных мышей.
    public readonly int ThreeStarShots;
    public readonly int TwoStarShots;

    public LevelDefinition(
        int id,
        string displayName,
        string tagline,
        string sceneName,
        string previewResourcePath,
        bool isImplemented,
        int requiredStars = 0,
        int threeStarShots = 1,
        int twoStarShots = 2)
    {
        Id = id;
        DisplayName = displayName;
        Tagline = tagline;
        SceneName = sceneName;
        PreviewResourcePath = previewResourcePath;
        IsImplemented = isImplemented;
        RequiredStars = Mathf.Max(0, requiredStars);
        ThreeStarShots = Mathf.Max(1, threeStarShots);
        TwoStarShots = Mathf.Max(ThreeStarShots, twoStarShots);
    }

    public bool CanLoad =>
        IsImplemented && !string.IsNullOrEmpty(SceneName);
}
