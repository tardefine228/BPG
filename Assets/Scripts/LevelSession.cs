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
    [SerializeField] private string lobbySceneName = LevelProgress.LobbySceneName;
    [SerializeField] private float completeDelay = 1.1f;

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
    private bool scoreCounts;
    private bool completionStarted;
    private bool completed;
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
        targets = FindObjectsByType<LevelTarget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        RefreshScoreHud();
    }

    private void Update()
    {
        if (!completed)
        {
            RefreshScoreHud();
        }

        if (completionStarted || targets == null || targets.Length == 0)
        {
            return;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null || !targets[i].IsCleared)
            {
                return;
            }
        }

        completionStarted = true;
        StartCoroutine(CompleteAfterDelay());
    }

    private IEnumerator CompleteAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0.15f, completeDelay));
        CompleteLevel();
    }

    private void CompleteLevel()
    {
        if (completed)
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
        Time.timeScale = 1f;
        SceneManager.LoadScene(string.IsNullOrEmpty(lobbySceneName)
            ? LevelProgress.LobbySceneName
            : lobbySceneName);
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
            new Vector2(640f, 88f), new Vector2(0f, -160f), ReturnToLobby, 42f);
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
        RectTransform rect = CreateRect(
            objectName,
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            size,
            position);

        Image background = rect.gameObject.AddComponent<Image>();
        background.sprite = uiSprite;
        background.color = Color.white;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = MenuColorBlock.Create();
        button.onClick.AddListener(onClick);

        RectTransform labelRect = CreateRect(
            "Label",
            rect,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            new Vector2(-18f, -10f),
            Vector2.zero);
        CreateText(labelRect, label, fontSize, new Color32(45, 45, 45, 255));
        return button;
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
