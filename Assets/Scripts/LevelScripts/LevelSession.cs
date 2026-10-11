using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Watches the current gameplay level, shows a live score, awards stars on every
/// clear and grants score only for the first completion.
/// </summary>
[DisallowMultipleComponent]
public sealed class LevelSession : MonoBehaviour
{
    [SerializeField] private SlingshotController gameplayController;
    [SerializeField] private float completeDelay = 1.1f;

    [Header("Боезапас этого уровня")]
    [Tooltip("Максимум снарядов за попытку. Меняется отдельно в каждой сцене на GameManager.")]
    [SerializeField, Min(1)] private int maxProjectiles = 5;
    [Tooltip("Сколько секунд ждать после остановки снарядов и ящиков перед поражением.")]
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

    private const int HudSortingOrder = 80;
    private const int OverlaySortingOrder = 90;
    private const string Victory3StarsPath = "Audio/Victory/Victory3Stars";
    private const string Victory2StarsPath = "Audio/Victory/Victory2Stars";
    private const string Victory1StarsPath = "Audio/Victory/Victory1Stars";

    private LevelTarget[] targets;
    private Sprite uiSprite;
    private Sprite starOnSprite;
    private Sprite starOffSprite;
    private TMP_FontAsset uiFont;
    private TMP_Text scoreValueText;
    private TMP_Text scoreHintText;
    private TMP_Text ammoValueText;
    private bool scoreCounts;
    private bool completionStarted;
    private bool completed;
    private bool failed;
    private bool checkingDefeat;
    private int displayedScore;
    private AudioSource victorySource;

    private int LevelId => forcedLevelId > 0 ? forcedLevelId : LevelProgress.CurrentLevelId;

    private void Awake()
    {
        if (gameplayController == null)
        {
            gameplayController = GetComponent<SlingshotController>();
        }

        LevelProgress.RestoreSelectedLevel();
        scoreCounts = !LevelProgress.HasAwardedScore(LevelId);
        BuildScoreHud();
    }

    private void Start()
    {
        targets = FindObjectsByType<LevelTarget>(FindObjectsInactive.Exclude);
        RefreshScoreHud();
    }

    private void Update()
    {
        if (!completed && !failed)
        {
            RefreshScoreHud();
            if (ammoValueText != null)
                ammoValueText.text = RemainingShots + " / " + MaxProjectiles;
        }

        if (IsEnding || targets == null || targets.Length == 0)
        {
            return;
        }

        // Victory is checked first, including a hit by the fifth projectile.
        if (AllTargetsCleared())
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

    private bool AllTargetsCleared()
    {
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
            // Wait for Update/FixedUpdate of every target before deciding the result.
            yield return null;
            if (IsEnding || AllTargetsCleared())
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
        foreach (LevelTarget target in targets)
        {
            if (target == null || target.IsCleared)
                continue;
            Rigidbody2D body = target.GetComponent<Rigidbody2D>();
            if (body == null || !body.simulated || body.bodyType != RigidbodyType2D.Dynamic || body.IsSleeping())
                continue;
            if (body.linearVelocity.sqrMagnitude > targetSettleSpeed * targetSettleSpeed
                || Mathf.Abs(body.angularVelocity) > targetSettleAngularSpeed)
                return true;
        }
        return false;
    }

    private void FailLevel()
    {
        if (IsEnding || AllTargetsCleared())
            return;
        failed = true;
        gameplayController.CancelInput();
        gameplayController.enabled = false;
        GetComponent<PauseMenuController>()?.SetResultScreenOpen();
        BuildDefeatOverlay();
        Time.timeScale = 0f;
    }

    private void RestartLevel()
    {
        AudioManager.Instance?.PlayUiClick();
        Time.timeScale = 1f;
        SceneTransition.Load(SceneManager.GetActiveScene().name);
    }

    private IEnumerator CompleteAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0.15f, completeDelay));
        // Let the impact and camera return finish before covering the destruction.
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

        EvaluateTargets(out int clearedBoxes, out float travelDistance);
        int stars = LevelProgress.StarsForShots(LevelId, shots);
        int runScore = LevelProgress.ComputeRunScore(clearedBoxes, shots, travelDistance);
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
        BuildCompletionOverlay(stars, awardedScore ? runScore : 0, awardedScore, shots);
        PlayVictorySound(stars);
    }

    /// <summary>Plays the jingle that matches the earned star count.</summary>
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
        // Unscaled time: the jingle must land even if the game is paused.
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

        // No manager in the scene (for example a bare test scene): fall back to
        // a local source so the jingle is still audible.
        if (victorySource == null)
        {
            victorySource = gameObject.AddComponent<AudioSource>();
            victorySource.playOnAwake = false;
            victorySource.loop = false;
            victorySource.spatialBlend = 0f;
            victorySource.dopplerLevel = 0f;
        }

        victorySource.PlayOneShot(clip, victoryVolume);
    }

    private void EvaluateTargets(out int clearedBoxes, out float travelDistance)
    {
        clearedBoxes = 0;
        travelDistance = 0f;
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
        EvaluateTargets(out int clearedBoxes, out float travelDistance);
        return LevelProgress.ComputeRunScore(clearedBoxes, shots, travelDistance);
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

    private void ReturnToLobby()
    {
        AudioManager.Instance?.PlayUiClick();
        Time.timeScale = 1f;
        SceneTransition.Load(LevelProgress.MainMenuSceneName);
    }

    private void GoToNextLevel()
    {
    if (SceneTransition.IsLoading)
        return;

    int nextLevelId = LevelId + 1;

    foreach (LevelDefinition level in LevelProgress.Levels)
    {
        if (level.Id == nextLevelId && level.CanLoad)
        {
            AudioManager.Instance?.PlayUiClick();
            Time.timeScale = 1f;

            LevelProgress.SelectLevel(level.Id);
            SceneTransition.Load(level.SceneName);
            return;
        }
    }

    ReturnToLobby();
}

    private void OnDestroy()
    {
        if (uiSprite != null)
        {
            Destroy(uiSprite);
        }

        if (starOnSprite != null)
        {
            Destroy(starOnSprite.texture);
            Destroy(starOnSprite);
        }

        if (starOffSprite != null)
        {
            Destroy(starOffSprite.texture);
            Destroy(starOffSprite);
        }
    }

    private void BuildScoreHud()
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0)
        {
            uiLayer = 5;
        }

        EnsureUiAssets();

        GameObject canvasObject = new GameObject(
            "LevelScoreCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.layer = uiLayer;
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = HudSortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform panel = CreateRect(
            "ScorePanel",
            canvasObject.transform,
            Vector2.up,
            Vector2.up,
            Vector2.up,
            new Vector2(280f, 92f),
            new Vector2(38f, -196f));
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = uiSprite;
        panelImage.color = Color.white;
        panelImage.raycastTarget = false;

        CreateLabel(panel, "ScoreTitle", "ОЧКИ", new Vector2(0f, 22f),
            new Vector2(250f, 32f), 22f, new Color32(45, 45, 45, 255));
        scoreValueText = CreateLabel(panel, "ScoreValue", "0", new Vector2(0f, -6f),
            new Vector2(250f, 40f), 34f, new Color32(45, 45, 45, 255));
        scoreHintText = CreateLabel(panel, "ScoreHint",
            scoreCounts ? "ПЕРВОЕ ПРОХОЖДЕНИЕ" : "НЕ НАЧИСЛЯЮТСЯ",
            new Vector2(0f, -32f),
            new Vector2(260f, 22f), 14f,
            scoreCounts ? new Color32(230, 155, 82, 255) : new Color32(140, 140, 140, 255));

        RectTransform ammoPanel = CreateRect("AmmoPanel", canvasObject.transform,
            Vector2.up, Vector2.up, Vector2.up, new Vector2(280f, 80f), new Vector2(38f, -304f));
        Image ammoImage = ammoPanel.gameObject.AddComponent<Image>();
        ammoImage.sprite = uiSprite;
        ammoImage.color = Color.white;
        ammoImage.raycastTarget = false;
        CreateLabel(ammoPanel, "AmmoTitle", "СНАРЯДЫ", new Vector2(0f, 19f),
            new Vector2(250f, 30f), 20f, new Color32(45, 45, 45, 255));
        ammoValueText = CreateLabel(ammoPanel, "AmmoValue", MaxProjectiles + " / " + MaxProjectiles,
            new Vector2(0f, -15f), new Vector2(250f, 40f), 32f, new Color32(230, 155, 82, 255));
    }

    private void BuildDefeatOverlay()
    {
        EnsureUiAssets();
        GameObject canvasObject = new GameObject("LevelDefeatCanvas", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.layer = 5;
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = OverlaySortingOrder;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform overlay = CreateRect("Overlay", canvasObject.transform, Vector2.zero,
            Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Image shade = overlay.gameObject.AddComponent<Image>();
        shade.sprite = uiSprite;
        shade.color = new Color32(0, 0, 0, 190);
        shade.raycastTarget = true;

        RectTransform panel = CreateRect("DefeatPanel", overlay, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(860f, 490f), Vector2.zero);
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = MenuButtonStyle.Default.buttonSprite;
        panelImage.type = Image.Type.Sliced;
        panelImage.color = new Color32(245, 242, 235, 255);
        CreateLabel(panel, "Title", "ПОРАЖЕНИЕ", new Vector2(0f, 163f),
            new Vector2(780f, 76f), 56f, new Color32(45, 45, 45, 255));
        CreateLabel(panel, "Hint", "Снаряды закончились. Попробуй ещё раз!", new Vector2(0f, 81f),
            new Vector2(770f, 70f), 28f, new Color32(95, 90, 85, 255));
        CreateButton(panel, "RestartButton", "НАЧАТЬ ЗАНОВО", new Vector2(640f, 86f),
            new Vector2(0f, -24f), RestartLevel, 34f);
        CreateButton(panel, "MainMenuButton", "ГЛАВНОЕ МЕНЮ", new Vector2(640f, 86f),
            new Vector2(0f, -129f), ReturnToLobby, 34f);
    }

    private void BuildCompletionOverlay(int stars, int score, bool awardedScore, int shots)
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0)
        {
            uiLayer = 5;
        }

        EnsureUiAssets();
        starOnSprite = StarSpriteFactory.Create(true);
        starOffSprite = StarSpriteFactory.Create(false);

        GameObject canvasObject = new GameObject(
            "LevelCompleteCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.layer = uiLayer;

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = OverlaySortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform overlay = CreateRect(
            "Overlay",
            canvasObject.transform,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero);
        Image overlayImage = overlay.gameObject.AddComponent<Image>();
        overlayImage.sprite = uiSprite;
        overlayImage.color = new Color32(0, 0, 0, 205);
        overlayImage.raycastTarget = true;

        CreateLabel(overlay, "Title", "УРОВЕНЬ ПРОЙДЕН", new Vector2(0f, 250f),
            new Vector2(1000f, 90f), 58f, Color.white);

        CreateStarRow(overlay, stars, new Vector2(0f, 120f));

        string scoreLine = awardedScore
            ? "ОЧКИ  " + score
            : "ОЧКИ УЖЕ ПОЛУЧЕНЫ";
        CreateLabel(overlay, "ScoreLine", scoreLine, new Vector2(0f, 20f),
            new Vector2(900f, 60f), 36f, awardedScore
                ? new Color32(230, 155, 82, 255)
                : new Color32(200, 200, 200, 255));

        int threeStarShots = LevelProgress.ThreeStarShots(LevelId);
        CreateLabel(overlay, "ShotsLine",
            "ВЫСТРЕЛОВ: " + shots + "      3 ЗВЕЗДЫ — ДО " + threeStarShots + " "
            + ShotsWord(threeStarShots),
            new Vector2(0f, -58f), new Vector2(1100f, 44f), 25f,
            new Color32(190, 190, 190, 255));

        CreateButton(overlay, "ContinueButton", "ДАЛЬШЕ",
            new Vector2(640f, 88f), new Vector2(0f, -160f), GoToNextLevel, 42f);
    }

    private static string ShotsWord(int shots)
    {
        return shots == 1 ? "ВЫСТРЕЛА" : "ВЫСТРЕЛОВ";
    }

    private void EnsureUiAssets()
    {
        if (uiSprite == null)
        {
            uiSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f));
        }

        if (uiFont == null)
        {
            uiFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }
    }

    private void CreateStarRow(Transform parent, int filledStars, Vector2 position)
    {
        RectTransform row = CreateRect(
            "Stars",
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(360f, 96f),
            position);

        float spacing = 110f;
        float startX = -spacing;
        for (int i = 0; i < LevelProgress.MaxStars; i++)
        {
            RectTransform starRect = CreateRect(
                "Star" + (i + 1),
                row,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(88f, 88f),
                new Vector2(startX + spacing * i, 0f));
            Image image = starRect.gameObject.AddComponent<Image>();
            image.sprite = i < filledStars ? starOnSprite : starOffSprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;
        }
    }

    private Button CreateButton(
        Transform parent,
        string objectName,
        string label,
        Vector2 size,
        Vector2 position,
        UnityAction onClick,
        float fontSize)
    {
        return MenuButtonStyle.Default.CreateButton(parent, objectName, label,
            new Vector2(0.5f, 0.5f), size, position, onClick, fontSize);
    }

    private TMP_Text CreateLabel(
        Transform parent,
        string objectName,
        string value,
        Vector2 position,
        Vector2 size,
        float fontSize,
        Color color)
    {
        RectTransform rect = CreateRect(
            objectName,
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            size,
            position);
        return CreateText(rect, value, fontSize, color);
    }

    private TMP_Text CreateText(RectTransform parent, string value, float fontSize, Color color)
    {
        TextMeshProUGUI text = parent.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = uiFont;
        text.fontSize = fontSize;
        text.fontSizeMin = Mathf.Max(10f, fontSize - 12f);
        text.fontSizeMax = fontSize;
        text.enableAutoSizing = true;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.raycastTarget = false;
        text.margin = new Vector4(4f, 1f, 4f, 1f);
        return text;
    }

    private static RectTransform CreateRect(
        string objectName,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 size,
        Vector2 anchoredPosition)
    {
        GameObject uiObject = new GameObject(objectName, typeof(RectTransform));
        uiObject.layer = parent.gameObject.layer;

        RectTransform rect = uiObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        return rect;
    }
}
