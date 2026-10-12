using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Уровень заканчивается в двух случаях: убиты все обязательные мыши либо у игрока
// закончились коты — тогда подводится частичный результат по убитым мышам.
[DisallowMultipleComponent]
public sealed class LevelSession : MonoBehaviour
{
    [Header("Очки за попадания")]
    [Tooltip("Очки за каждый засчитанный сильный удар по коробке LevelTarget.")]
    [SerializeField, Min(0)] private int boxHitPoints = 150;
    [Tooltip("Очки за каждый удар, снявший HP у мыши.")]
    [SerializeField, Min(0)] private int enemyHitPoints = 250;
    [Tooltip("Дополнительные очки за уничтоженную мышь (в том числе при падении).")]
    [SerializeField, Min(0)] private int enemyKillPoints = 500;
    [Tooltip("Очки за полное разрушение цели или её падение за карту.")]
    [SerializeField, Min(0)] private int targetClearPoints = 300;
    [Tooltip("Победный бонус за каждого НЕ использованного кота. Во время выстрелов счёт НЕ меняет.")]
    [SerializeField, Min(0)] private int unusedCatVictoryBonus = 200;

    [Header("Старт уровня")]
    [Tooltip("Сколько игровых секунд с начала уровня очки не начисляются. Считается только время, " +
             "когда игра идёт: пауза и обучение эту задержку не сокращают.")]
    [SerializeField, Min(0f)] private float scoreGraceSeconds = 2f;

    [SerializeField] private SlingshotController gameplayController;
    [SerializeField] private float completeDelay = 1.1f;

    [Header("Боезапас и оценка результата")]
    [Tooltip("Максимум снарядов за попытку. Меняется отдельно в каждой сцене на GameManager.")]
    [SerializeField, Min(1)] private int maxProjectiles = 5;
    [Tooltip("Минимум игровых секунд от последнего выстрела до подсчёта результата, если все цели ещё не выполнены.")]
    [SerializeField, Min(5f)] private float resultDelayAfterLastShot = 5f;
    [Tooltip("После этой паузы без движения можно безопасно подвести итог: коробки и снаряды уже не должны добить мышь.")]
    [SerializeField, Min(0.2f)] private float resultSettleDelay = 1f;
    [SerializeField, Min(0.01f)] private float targetSettleSpeed = 0.15f;
    [SerializeField, Min(0.1f)] private float targetSettleAngularSpeed = 12f;

    // Максимальное дополнительное ожидание после последнего выстрела: если постройка
    // продолжает медленно катиться, уровень всё равно заканчивается.
    private const float MaxResultExtraWait = 4f;

    public int MaxProjectiles => Mathf.Max(1, maxProjectiles);
    public int RemainingShots => Mathf.Max(0, MaxProjectiles
        - (gameplayController != null ? gameplayController.ShotsFired : 0));
    public bool IsEnding => completionStarted || completed || failed;

    [Tooltip("Номер уровня этой сцены. 0 — взять уровень, выбранный в меню. " +
             "Позволяет запускать карту прямо из редактора.")]
    [SerializeField] private int forcedLevelId;

    [Header("Звук победы")]
    [Tooltip("Фанфары за 3 звезды. Если поле пустое, клип берётся из Resources.")]
    [SerializeField] private AudioClip victory3StarsSound;

    [Tooltip("Джингл за 2 звезды. Если поле пустое, клип берётся из Resources.")]
    [SerializeField] private AudioClip victory2StarsSound;

    [Tooltip("Скромный джингл за 1 звезду. Если поле пустое, клип берётся из Resources.")]
    [SerializeField] private AudioClip victory1StarsSound;

    [Tooltip("Задержка перед звуком победы, чтобы он совпал с появлением итогового окна.")]
    [SerializeField, Min(0f)] private float victorySoundDelay = 0.15f;

    [SerializeField, Range(0f, 1f)] private float victoryVolume = 0.9f;

    private const string Victory3StarsPath = "Audio/Effects/Victory3Stars";
    private const string Victory2StarsPath = "Audio/Effects/Victory2Stars";
    private const string Victory1StarsPath = "Audio/Effects/Victory1Stars";

    private LevelTarget[] targets;
    private Rigidbody2D[] targetBodies;
    private MouseEnemy[] enemies;
    private LevelObjective[] extraObjectives;
    private Rigidbody2D[] enemyBodies;
    private int earnedScore;
    private int victoryBonus;
    // Игровое время, прошедшее с начала уровня (масштабируется Time.timeScale,
    // поэтому во время паузы и обучения не растёт).
    private float playTimeElapsed;
    [SerializeField] private Sprite starOnSprite;
    [SerializeField] private Sprite starOffSprite;
    [SerializeField] private TMP_Text scoreValueText;
    [SerializeField] private TMP_Text scoreHintText;
    [SerializeField] private GameObject completionOverlay;
    [SerializeField] private GameObject defeatOverlay;
    [SerializeField] private TMP_Text completionScoreText;
    [SerializeField] private TMP_Text completionShotsText;
    [SerializeField] private Image[] completionStars;
    private bool scoreCounts;
    private bool completionStarted;
    private bool completed;
    private bool failed;
    private bool checkingResult;
    private int displayedScore;
    [SerializeField] private AudioSource victorySource;

    private int LevelId => forcedLevelId > 0 ? forcedLevelId : LevelProgress.CurrentLevelId;

    private void Awake()
    {
        if (gameplayController == null)
        {
            gameplayController = GetComponent<SlingshotController>();
        }

        LevelProgress.RestoreSelectedLevel();
        scoreCounts = !LevelProgress.HasAwardedScore(LevelId);
        completionOverlay.SetActive(false);
        defeatOverlay.SetActive(false);
    }

    private void Start()
    {
        targets = FindObjectsByType<LevelTarget>(FindObjectsInactive.Exclude);
        targetBodies = new Rigidbody2D[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            targetBodies[i] = targets[i].GetComponent<Rigidbody2D>();
            targets[i].HitRegistered += OnBoxHit;
            targets[i].Cleared += OnTargetCleared;
        }

        enemies = FindObjectsByType<MouseEnemy>(FindObjectsInactive.Exclude);
        enemyBodies = new Rigidbody2D[enemies.Length];
        for (int i = 0; i < enemies.Length; i++)
        {
            enemyBodies[i] = enemies[i].GetComponent<Rigidbody2D>();
            enemies[i].Damaged += OnEnemyDamaged;
            enemies[i].Died += OnEnemyDied;
        }
        extraObjectives = FindObjectsByType<LevelObjective>(FindObjectsInactive.Exclude);
        foreach (LevelObjective objective in extraObjectives)
            objective.Completed += OnExtraObjectiveCompleted;

        RefreshScoreHud();
    }

    private void OnDestroy()
    {
        if (extraObjectives != null)
            foreach (LevelObjective objective in extraObjectives)
                if (objective != null)
                    objective.Completed -= OnExtraObjectiveCompleted;

        if (targets != null)
            foreach (LevelTarget target in targets)
                if (target != null)
                {
                    target.HitRegistered -= OnBoxHit;
                    target.Cleared -= OnTargetCleared;
                }

        if (enemies != null)
            foreach (MouseEnemy enemy in enemies)
                if (enemy != null)
                {
                    enemy.Damaged -= OnEnemyDamaged;
                    enemy.Died -= OnEnemyDied;
                }
    }

    private void OnExtraObjectiveCompleted(LevelObjective objective)
    {
        AwardPoints(objective.CompletionPoints);
    }

    private void OnBoxHit(LevelTarget target, float speed)
    {
        AwardPoints(boxHitPoints);
    }

    private void OnTargetCleared(LevelTarget target)
    {
        AwardPoints(targetClearPoints);
    }

    private void OnEnemyDamaged(MouseEnemy enemy, float damage)
    {
        if (damage > 0f)
            AwardPoints(enemyHitPoints);
    }

    private void OnEnemyDied(MouseEnemy enemy)
    {
        AwardPoints(enemyKillPoints);
    }

    // В первые секунды уровня попадания и падения не приносят очков.
    private bool IsScoreGraceActive => playTimeElapsed < Mathf.Max(0f, scoreGraceSeconds);

    private void AwardPoints(int points)
    {
        if (points <= 0 || completed || failed || IsScoreGraceActive)
            return;
        earnedScore += points;
        RefreshScoreHud();
    }

    private void Update()
    {
        if (!completed && !failed)
        {
            if (IsScoreGraceActive)
                playTimeElapsed += Time.deltaTime;
            RefreshScoreHud();
        }

        if (IsEnding || !HasObjectives())
            return;

        // Уровень заканчивается сразу, как только убита последняя обязательная мышь.
        // Постройки при этом могут остаться целыми: они дают очки, но не мешают победе.
        if (VictoryReached())
        {
            completionStarted = true;
            gameplayController?.CancelInput();
            StartCoroutine(CompleteAfterDelay());
        }
        // Второй конец уровня — когда запускать больше некого: коты закончились,
        // а мыши остались. Тогда подводится частичный результат по убитым мышам.
        else if (gameplayController != null && RemainingShots == 0 && !checkingResult)
        {
            checkingResult = true;
            StartCoroutine(EvaluateAfterLastShot());
        }
    }

    private bool HasObjectives()
    {
        if (extraObjectives != null)
            for (int i = 0; i < extraObjectives.Length; i++)
                if (extraObjectives[i] != null && extraObjectives[i].CountsForVictory)
                    return true;
        if (enemies != null)
            for (int i = 0; i < enemies.Length; i++)
                if (enemies[i] != null && enemies[i].CountsForVictory)
                    return true;
        if (targets != null)
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] != null && targets[i].CountsForVictory)
                    return true;
        return false;
    }

    // Победа: убиты все обязательные мыши. Ящики, планки и прочие постройки
    // обязательными не считаются — на карте без обязательных мышей победа
    // по-прежнему требует разобрать обязательные цели.
    private bool VictoryReached()
    {
        GetMouseKillCounts(out int defeated, out int total);
        return total > 0 ? defeated >= total : AllObjectivesCleared();
    }

    private bool AllObjectivesCleared()
    {
        if (extraObjectives != null)
            for (int i = 0; i < extraObjectives.Length; i++)
                if (extraObjectives[i] != null && extraObjectives[i].CountsForVictory
                    && !extraObjectives[i].IsCompleted)
                    return false;

        if (enemies != null)
            for (int i = 0; i < enemies.Length; i++)
                if (enemies[i] != null && enemies[i].CountsForVictory && !enemies[i].IsDead)
                    return false;

        if (targets != null)
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] != null && targets[i].CountsForVictory && !targets[i].IsCleared)
                    return false;

        return true;
    }

    private IEnumerator EvaluateAfterLastShot()
    {
        // Пять секунд считаются от последнего выстрела, а не от остановки
        // коробок: котам нужно время долететь и вызвать цепные столкновения.
        float evaluateAt = Time.time + Mathf.Max(0f, resultDelayAfterLastShot);
        float stillFor = 0f;
        while (!IsEnding && RemainingShots == 0)
        {
            // Даёт физике и событиям мышей обновиться после шага симуляции.
            yield return null;
            if (IsEnding || VictoryReached())
                break;

            bool moving = gameplayController.IsShotInProgress || gameplayController.HasMovingProjectiles
                || (gameplayController.gameplayCamera != null && gameplayController.gameplayCamera.IsBusy)
                || TargetsAreMoving();
            stillFor = moving ? 0f : stillFor + Time.deltaTime;

            // Если на пятой секунде ещё летит кот или двигается постройка,
            // ждём их завершения и короткой тишины, чтобы не оборвать цепную гибель.
            // Долго ждать нельзя: коты закончились, и уровень обязан закончиться,
            // даже если что-то продолжает медленно катиться.
            bool settled = stillFor >= Mathf.Max(0.2f, resultSettleDelay);
            bool waitedTooLong = Time.time >= evaluateAt + MaxResultExtraWait;
            if (Time.time >= evaluateAt && (settled || waitedTooLong))
            {
                int stars = GetMiceStars();
                if (stars > 0)
                    CompleteLevel(stars);
                else
                    FailLevel();
                break;
            }
        }
        checkingResult = false;
    }

    private int GetMiceStars()
    {
        GetMouseKillCounts(out int defeated, out int total);
        return LevelProgress.StarsForDefeatedMice(defeated, total);
    }

    private int GetCompletionStars(int shots)
    {
        GetMouseKillCounts(out int defeated, out int total);
        // На будущих картах без обязательных мышей сохраняется старое правило.
        return total > 0
            ? LevelProgress.StarsForDefeatedMice(defeated, total)
            : LevelProgress.StarsForShots(LevelId, shots);
    }

    private void GetMouseKillCounts(out int defeated, out int total)
    {
        defeated = 0;
        total = 0;
        if (enemies == null)
            return;

        for (int i = 0; i < enemies.Length; i++)
        {
            MouseEnemy enemy = enemies[i];
            if (enemy == null || !enemy.CountsForVictory)
                continue;

            total++;
            if (enemy.IsDead)
                defeated++;
        }
    }

    private bool TargetsAreMoving()
    {
        // Уже сбитые коробки тоже могут раздавить мышь, но коробки,
        // упавшие за нижнюю границу карты, не должны бесконечно откладывать поражение.
        float minRelevantY = gameplayController != null && gameplayController.gameplayCamera != null
            ? gameplayController.gameplayCamera.mapMin.y - 5f : -15f;
        for (int i = 0; i < targetBodies.Length; i++)
            if (targets[i] != null && targets[i].transform.position.y >= minRelevantY
                && BodyIsMoving(targetBodies[i]))
                return true;

        for (int i = 0; i < enemyBodies.Length; i++)
            if (enemies[i] != null && !enemies[i].IsDead && BodyIsMoving(enemyBodies[i]))
                return true;

        return false;
    }

    private bool BodyIsMoving(Rigidbody2D body)
    {
        return body != null && body.simulated
            && body.bodyType == RigidbodyType2D.Dynamic && !body.IsSleeping()
            && (body.linearVelocity.sqrMagnitude > targetSettleSpeed * targetSettleSpeed
                || Mathf.Abs(body.angularVelocity) > targetSettleAngularSpeed);
    }

    private void FailLevel()
    {
        if (IsEnding || VictoryReached())
            return;
        failed = true;
        gameplayController.CancelInput();
        gameplayController.enabled = false;
        GetComponent<PauseMenuController>()?.SetResultScreenOpen();
        defeatOverlay.SetActive(true);
        Time.timeScale = 0f;
    }

    public void RestartLevel()
    {
        AudioManager.Instance?.PlayUiClick();
        Time.timeScale = 1f;
        SceneTransition.Load(SceneManager.GetActiveScene().name);
    }

    private IEnumerator CompleteAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0.15f, completeDelay));
        // Не перекрывает стартовое приближение итоговым окном.
        while (gameplayController != null && gameplayController.gameplayCamera != null
            && gameplayController.gameplayCamera.IsBusy)
            yield return null;
        int shots = gameplayController != null ? gameplayController.ShotsFired : 1;
        CompleteLevel(GetCompletionStars(Mathf.Max(1, shots)));
    }

    private void CompleteLevel(int stars)
    {
        if (completed || failed)
        {
            return;
        }

        // Победный экран всегда содержит минимум одну и максимум три звезды.
        stars = Mathf.Clamp(stars, 1, LevelProgress.MaxStars);
        completed = true;

        int shots = gameplayController != null ? gameplayController.ShotsFired : 1;
        if (shots < 1)
        {
            shots = 1;
        }

        GetMouseKillCounts(out int defeatedMice, out int totalMice);
        victoryBonus = Mathf.Max(0, MaxProjectiles - shots) * unusedCatVictoryBonus;
        int runScore = earnedScore + victoryBonus;
        displayedScore = runScore;

        LevelProgress.SaveStars(LevelId, stars);
        bool awardedScore = false;
        if (scoreCounts)
        {
            awardedScore = LevelProgress.TryAwardFirstClearScore(LevelId, runScore);
        }

        LevelProgress.MarkCompleted(LevelId);
        RefreshScoreHud();

        if (gameplayController != null)
        {
            gameplayController.CancelInput();
            gameplayController.enabled = false;
        }

        GetComponent<PauseMenuController>()?.SetResultScreenOpen();
        ShowCompletionOverlay(stars, runScore, awardedScore, shots, defeatedMice, totalMice);
        PlayVictorySound(stars);
    }

    // Выбирает звук победы по числу полученных звёзд.
    private void PlayVictorySound(int stars)
    {
        AudioClip clip = stars >= 3
            ? (victory3StarsSound != null ? victory3StarsSound
                : Resources.Load<AudioClip>(Victory3StarsPath))
            : stars == 2
                ? (victory2StarsSound != null ? victory2StarsSound
                    : Resources.Load<AudioClip>(Victory2StarsPath))
                : (victory1StarsSound != null ? victory1StarsSound
                    : Resources.Load<AudioClip>(Victory1StarsPath));
        if (clip == null)
        {
            return;
        }

        if (victorySoundDelay > 0f)
        {
            StartCoroutine(PlayVictoryAfterDelay(clip));
            return;
        }

        PlayVictoryClip(clip);
    }

    private IEnumerator PlayVictoryAfterDelay(AudioClip clip)
    {
        // Задержка звука работает и при остановленном игровом времени.
        yield return new WaitForSecondsRealtime(victorySoundDelay);
        PlayVictoryClip(clip);
    }

    private void PlayVictoryClip(AudioClip clip)
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySfx(clip, victoryVolume);
            return;
        }

        // В тестовой сцене без AudioManager используется заранее назначенный источник.
        if (victorySource != null)
            victorySource.PlayOneShot(clip, victoryVolume);
    }

    private void RefreshScoreHud()
    {
        if (scoreValueText == null)
        {
            return;
        }

        displayedScore = earnedScore + victoryBonus;
        scoreValueText.text = displayedScore.ToString();
        if (scoreHintText != null)
        {
            scoreHintText.text = scoreCounts ? "ПЕРВОЕ ПРОХОЖДЕНИЕ" : string.Empty;
        }
    }

    // Из победного/проигрышного окна возвращает не в главное меню,
    // а сразу в меню выбора уровней.
    public void ReturnToLevelSelect()
    {
        AudioManager.Instance?.PlayUiClick();
        Time.timeScale = 1f;
        MenuManager.ReturnToLevelSelect();
    }

    public void GoToNextLevel()
    {
        if (SceneTransition.IsLoading)
            return;
        foreach (LevelDefinition level in LevelProgress.Levels)
        {
            // Если следующий уровень ещё закрыт порогом звёзд, кнопка возвращает
            // к выбору уровня: там видно, сколько звёзд до него не хватает.
            if (level.Id != LevelId + 1 || !level.CanLoad || !LevelProgress.IsUnlocked(level.Id))
                continue;
            AudioManager.Instance?.PlayUiClick();
            Time.timeScale = 1f;
            LevelProgress.SelectLevel(level.Id);
            SceneTransition.Load(level.SceneName);
            return;
        }
        ReturnToLevelSelect();
    }

    // Итоговое окно уже находится в сцене; здесь меняются только значения.
    private void ShowCompletionOverlay(int stars, int score, bool awardedScore, int shots,
        int defeatedMice, int totalMice)
    {
        // Подписи о повторном прохождении нет; о нём говорит только цвет очков.
        completionScoreText.text = "ОЧКИ  " + score;
        completionScoreText.color = awardedScore ? new Color32(230, 155, 82, 255)
            : new Color32(200, 200, 200, 255);
        // Выстрелы показываются как «использовано / весь боезапас»: «ВЫСТРЕЛОВ: 2 / 3».
        string shotsLine = "ВЫСТРЕЛОВ: " + shots + " / " + MaxProjectiles;
        completionShotsText.text = totalMice > 0
            ? "ПОБЕЖДЕНО МЫШЕЙ: " + defeatedMice + " / " + totalMice + "      " + shotsLine
            : shotsLine;
        for (int i = 0; i < completionStars.Length; i++)
            completionStars[i].sprite = i < stars ? starOnSprite : starOffSprite;
        completionOverlay.SetActive(true);
    }

}
