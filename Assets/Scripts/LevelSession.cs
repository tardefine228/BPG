using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Watches the current gameplay level, awards stars when every target is cleared
/// and returns the player to the map lobby.
/// </summary>
[DisallowMultipleComponent]
public sealed class LevelSession : MonoBehaviour
{
    [SerializeField] private SlingshotController gameplayController;
    [SerializeField] private string lobbySceneName = LevelProgress.LobbySceneName;
    [SerializeField] private float completeDelay = 1.1f;

    private const int OverlaySortingOrder = 90;

    private LevelTarget[] targets;
    private Sprite uiSprite;
    private Sprite starOnSprite;
    private Sprite starOffSprite;
    private TMP_FontAsset uiFont;
    private bool completionStarted;
    private bool completed;

    private void Awake()
    {
        if (gameplayController == null)
        {
            gameplayController = GetComponent<SlingshotController>();
        }

        LevelProgress.RestoreSelectedLevel();
    }

    private void Start()
    {
        targets = FindObjectsByType<LevelTarget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
    }

    private void Update()
    {
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

        int stars = LevelProgress.StarsFromShots(shots);
        LevelProgress.SaveStars(LevelProgress.CurrentLevelId, stars);

        if (gameplayController != null)
        {
            gameplayController.CancelInput();
            gameplayController.enabled = false;
        }

        BuildCompletionOverlay(stars);
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

    private void BuildCompletionOverlay(int stars)
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0)
        {
            uiLayer = 5;
        }

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

        uiSprite = Sprite.Create(
            Texture2D.whiteTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f));
        uiFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        starOnSprite = StarSpriteFactory.Create(true);
        starOffSprite = StarSpriteFactory.Create(false);

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

        CreateLabel(overlay, "Title", "УРОВЕНЬ ПРОЙДЕН", new Vector2(0f, 210f),
            new Vector2(1000f, 90f), 58f, Color.white);

        CreateStarRow(overlay, stars, new Vector2(0f, 70f));

        CreateButton(overlay, "ContinueButton", "ДАЛЬШЕ",
            new Vector2(640f, 88f), new Vector2(0f, -160f), ReturnToLobby, 42f);
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

    private void CreateLabel(
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
        CreateText(rect, value, fontSize, color);
    }

    private TMP_Text CreateText(RectTransform parent, string value, float fontSize, Color color)
    {
        TextMeshProUGUI text = parent.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = uiFont;
        text.fontSize = fontSize;
        text.fontSizeMin = Mathf.Max(18f, fontSize - 12f);
        text.fontSizeMax = fontSize;
        text.enableAutoSizing = true;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.raycastTarget = false;
        text.margin = new Vector4(6f, 2f, 6f, 2f);
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
