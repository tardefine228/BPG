using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Проверяет результат уровня, показывает очки и начисляет награду только за первое прохождение.
// Уровень пройден, когда побеждены все мыши (MouseTarget). На уровне без мышей нужно сбить все блоки.
[DisallowMultipleComponent]
public sealed class LevelSession : MonoBehaviour
{
    [SerializeField] private SlingshotController gameplayController;
    [SerializeField] private float completeDelay = 1.1f;

    [Header("Боезапас этого уровня")]
    [Tooltip("Максимум снарядов за попытку. Меняется отдельно в каждой сцене на GameManager.")]
    [SerializeField, Min(1)] private int maxProjectiles = 5;
    [Tooltip("Сколько секунд ждать после остановки снарядов, блоков и мышей перед поражением.")]
    [SerializeField, Min(0.2f)] private float defeatSettleDelay = 1f;
    [SerializeField, Min(0.01f)] private float targetSettleSpeed = 0.15f;
    [SerializeField, Min(0.1f)] private float targetSettleAngularSpeed = 12f;

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
    private MouseTarget[] mice;
    [SerializeField] private Sprite starOnSprite;
    [SerializeField] private Sprite starOffSprite;
    [SerializeField] private TMP_Text scoreValueText;
    [SerializeField] private TMP_Text scoreHintText;
    [SerializeField] private TMP_Text ammoValueText;
    [SerializeField] private GameObject completionOverlay;
    [SerializeField] private GameObject defeatOverlay;
    [SerializeField] private TMP_Text completionScoreText;
    [SerializeField] private TMP_Text completionShotsText;
    [SerializeField] private Image[] completionStars;
    private bool scoreCounts;
    private bool completionStarted;
    private bool completed;
    private bool failed;
    private bool checkingDefeat;
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
            targetBodies[i] = targets[i].GetComponent<Rigidbody2D>();
        mice = FindObjectsByType<MouseTarget>(FindObjectsInactive.Exclude);
        RefreshScoreHud();
    }

    private bool HasGoal => (mice != null && mice.Length > 0) || (targets != null && targets.Length > 0);

    private void Update()
    {
        if (!completed && !failed)
        {
            RefreshScoreHud();
            if (ammoValueText != null)
                ammoValueText.text = RemainingShots + " / " + MaxProjectiles;
        }

        if (IsEnding || !HasGoal)
        {
            return;
        }

        // Сначала проверяется победа, в том числе попадание последним снарядом.
        if (LevelGoalReached())
        {
            completionStarted = true;
            gameplayController?.CancelInput();
            StartCoroutine(CompleteAfterDelay());
        }
        else if (gameplayController != null && RemainingShots == 0 && !checkingDefeat)
        {
            checkingDefeat = true;
            StartCoroutine(CheckDefeatAfterSettling());
        }
    }

    // Победа: побеждены все мыши. Уровень без мышей проходится по старому правилу — сбить все блоки.
    private bool LevelGoalReached()
    {
        if (mice != null && mice.Length > 0)
        {
            for (int i = 0; i < mice.Length; i++)
            {
                if (mice[i] != null && !mice[i].IsDefeated)
                    return false;
            }
            return true;
        }

        if (targets == null || targets.Length == 0)
            return false;
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null && !targets[i].IsCleared)
                return false;
        }
        return true;
    }

    private IEnumerator CheckDefeatAfterSettling()
    {
        float stillFor = 0f;
        while (!IsEnding && RemainingShots == 0)
        {
            // Даёт всем целям обновить состояние после физического шага.
            yield return null;
            if (IsEnding || LevelGoalReached())
                break;
            bool moving = gameplayController.IsShotInProgress || gameplayController.HasMovingProjectiles
                || (gameplayController.gameplayCamera != null && gameplayController.gameplayCamera.IsBusy)
                || TargetsAreMoving();
            stillFor = moving ? 0f : stillFor + Time.deltaTime;
            if (stillFor >= Mathf.Max(0.2f, defeatSettleDelay))
            {
                FailLevel();
                break;
            }
        }
        checkingDefeat = false;
    }

    private bool TargetsAreMoving()
    {
        for (int i = 0; i < targets.Length; i++)
        {
            LevelTarget target = targets[i];
            if (target == null || target.IsCleared)
                continue;
            if (IsMoving(targetBodies[i]))
                return true;
        }

        // Падающая мышь ещё может коснуться земли, поэтому поражение ждёт и её.
        for (int i = 0; mice != null && i < mice.Length; i++)
        {
            MouseTarget mouse = mice[i];
            if (mouse != null && !mouse.IsDefeated && IsMoving(mouse.Body))
                return true;
        }
        return false;
    }

    private bool IsMoving(Rigidbody2D body)
    {
        if (body == null || !body.simulated || body.bodyType != RigidbodyType2D.Dynamic || body.IsSleeping())
            return false;
        return body.linearVelocity.sqrMagnitude > targetSettleSpeed * targetSettleSpeed
            || Mathf.Abs(body.angularVelocity) > targetSettleAngularSpeed;
    }

    private void FailLevel()
    {
        if (IsEnding || LevelGoalReached())
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
        // Ожидает показа попадания и возвращения камеры перед итоговым окном.
        while (gameplayController != null && gameplayController.gameplayCamera != null
            && gameplayController.gameplayCamera.IsBusy)
            yield return null;
        CompleteLevel();
    }

    private void CompleteLevel()
    {
        if (completed || failed)
        {
            return;
        }

        completed = true;

        int shots = gameplayController != null ? gameplayController.ShotsFired : 1;
        if (shots < 1)
        {
            shots = 1;
        }

        EvaluateTargets(out int clearedBoxes, out int defeatedMice, out float travelDistance);
        int stars = LevelProgress.StarsForShots(LevelId, shots);
        int runScore = LevelProgress.ComputeRunScore(clearedBoxes, defeatedMice, shots, travelDistance);
        displayedScore = scoreCounts ? runScore : 0;

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
        ShowCompletionOverlay(stars, awardedScore ? runScore : 0, awardedScore, shots);
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

    private void EvaluateTargets(out int clearedBoxes, out int defeatedMice, out float travelDistance)
    {
        clearedBoxes = 0;
        defeatedMice = 0;
        travelDistance = 0f;
        if (mice != null)
        {
            for (int i = 0; i < mice.Length; i++)
            {
                if (mice[i] != null && mice[i].IsDefeated)
                    defeatedMice++;
            }
        }

        if (targets == null)
        {
            return;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            LevelTarget target = targets[i];
            if (target == null)
            {
                continue;
            }

            travelDistance += target.MaxTravelDistance;
            if (target.IsCleared)
            {
                clearedBoxes++;
            }
        }
    }

    private int CurrentRunScore()
    {
        if (!scoreCounts)
        {
            return 0;
        }

        int shots = gameplayController != null ? gameplayController.ShotsFired : 0;
        EvaluateTargets(out int clearedBoxes, out int defeatedMice, out float travelDistance);
        return LevelProgress.ComputeRunScore(clearedBoxes, defeatedMice, shots, travelDistance);
    }

    private void RefreshScoreHud()
    {
        if (scoreValueText == null)
        {
            return;
        }

        displayedScore = CurrentRunScore();
        scoreValueText.text = displayedScore.ToString();
        if (scoreHintText != null)
        {
            scoreHintText.text = scoreCounts ? "ПЕРВОЕ ПРОХОЖДЕНИЕ" : "НЕ НАЧИСЛЯЮТСЯ";
        }
    }

    public void ReturnToLobby()
    {
        AudioManager.Instance?.PlayUiClick();
        Time.timeScale = 1f;
        SceneTransition.Load(LevelProgress.MainMenuSceneName);
    }

    public void GoToNextLevel()
    {
        if (SceneTransition.IsLoading)
            return;
        foreach (LevelDefinition level in LevelProgress.Levels)
        {
            if (level.Id != LevelId + 1 || !level.CanLoad)
                continue;
            AudioManager.Instance?.PlayUiClick();
            Time.timeScale = 1f;
            LevelProgress.SelectLevel(level.Id);
            SceneTransition.Load(level.SceneName);
            return;
        }
        ReturnToLobby();
    }

    // Итоговое окно уже находится в сцене; здесь меняются только значения.
    private void ShowCompletionOverlay(int stars, int score, bool awardedScore, int shots)
    {
        completionScoreText.text = awardedScore ? "ОЧКИ  " + score : "ОЧКИ УЖЕ ПОЛУЧЕНЫ";
        completionScoreText.color = awardedScore ? new Color32(230, 155, 82, 255)
            : new Color32(200, 200, 200, 255);
        int par = LevelProgress.ThreeStarShots(LevelId);
        completionShotsText.text = "ВЫСТРЕЛОВ: " + shots + "      3 ЗВЕЗДЫ — ДО "
            + par + " " + ShotsWord(par);
        for (int i = 0; i < completionStars.Length; i++)
            completionStars[i].sprite = i < stars ? starOnSprite : starOffSprite;
        completionOverlay.SetActive(true);
    }
    private static string ShotsWord(int shots)
    {
        return shots == 1 ? "ВЫСТРЕЛА" : "ВЫСТРЕЛОВ";
    }

}
