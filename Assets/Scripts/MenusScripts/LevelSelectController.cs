using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Runtime-built map lobby. Each card shows the level name, a preview image and
/// earned stars. Later levels stay locked until the previous one is completed.
/// </summary>
[DisallowMultipleComponent]
public sealed class LevelSelectController : MonoBehaviour
{
    private const int CanvasSortingOrder = 10;

    [Tooltip("Открыть все уровни сразу — удобно для тестирования новых карт.")]
    [SerializeField] private bool unlockAllLevels;

    private Sprite uiSprite;
    private Sprite starOnSprite;
    private Sprite starOffSprite;
    private TMP_FontAsset uiFont;
    private GameObject comingSoonOverlay;

    private void Awake()
    {
        Time.timeScale = 1f;
        LevelProgress.UnlockAllLevels = unlockAllLevels;
        LevelProgress.RestoreSelectedLevel();
        BuildLobby();
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

    private void BuildLobby()
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0)
        {
            uiLayer = 5;
        }

        GameObject canvasObject = new GameObject(
            "LevelSelectCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.layer = uiLayer;

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortingOrder;

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

        RectTransform root = CreateRect(
            "Root",
            canvasObject.transform,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero);

        Image background = root.gameObject.AddComponent<Image>();
        ConfigureImage(background, new Color32(0, 0, 0, 255));

        CreateLabel(root, "Title", "ВЫБОР УРОВНЯ", new Vector2(0f, 430f),
            new Vector2(1100f, 90f), 58f, Color.white);

        LevelDefinition[] levels = LevelProgress.Levels;
        float spacing = 500f;
        float startX = -spacing * (levels.Length - 1) * 0.5f;
        for (int i = 0; i < levels.Length; i++)
        {
            CreateLevelCard(root, levels[i], new Vector2(startX + spacing * i, -20f));
        }

        comingSoonOverlay = BuildComingSoonOverlay(root);
        comingSoonOverlay.SetActive(false);
    }

    private void CreateLevelCard(Transform parent, LevelDefinition level, Vector2 position)
    {
        bool unlocked = LevelProgress.IsUnlocked(level.Id);
        int stars = LevelProgress.GetStars(level.Id);

        RectTransform card = CreateRect(
            "LevelCard_" + level.Id,
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(440f, 640f),
            position);

        Image cardImage = card.gameObject.AddComponent<Image>();
        ConfigureImage(cardImage, unlocked ? Color.white : new Color32(188, 188, 188, 255));

        Button button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = cardImage;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = MenuColorBlock.Create();
        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        button.interactable = unlocked;
        int levelId = level.Id;
        button.onClick.AddListener(() => OnLevelClicked(levelId));

        CreateLabel(card, "Name", level.DisplayName, new Vector2(0f, 278f),
            new Vector2(400f, 64f), 36f, new Color32(45, 45, 45, 255));

        RectTransform previewFrame = CreateRect(
            "PreviewFrame",
            card,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(392f, 360f),
            new Vector2(0f, 42f));
        Image frameImage = previewFrame.gameObject.AddComponent<Image>();
        ConfigureImage(frameImage, new Color32(32, 32, 32, 255));
        frameImage.raycastTarget = false;

        RectTransform previewRect = CreateRect(
            "Preview",
            previewFrame,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            new Vector2(-12f, -12f),
            Vector2.zero);
        Image previewImage = previewRect.gameObject.AddComponent<Image>();
        previewImage.sprite = LoadPreview(level.PreviewResourcePath);
        previewImage.preserveAspect = true;
        previewImage.raycastTarget = false;
        previewImage.color = unlocked ? Color.white : new Color32(140, 140, 140, 255);
        if (previewImage.sprite == null)
        {
            previewImage.sprite = uiSprite;
            previewImage.color = new Color32(60, 70, 90, 255);
        }

        if (!unlocked)
        {
            RectTransform lockOverlay = CreateRect(
                "LockOverlay",
                previewFrame,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            Image lockImage = lockOverlay.gameObject.AddComponent<Image>();
            ConfigureImage(lockImage, new Color32(0, 0, 0, 150));
            lockImage.raycastTarget = false;
            CreateLabel(lockOverlay, "LockLabel", "ЗАКРЫТО", Vector2.zero,
                new Vector2(360f, 80f), 34f, Color.white);
        }
        else if (!level.CanLoad)
        {
            RectTransform soonOverlay = CreateRect(
                "SoonOverlay",
                previewFrame,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            Image soonImage = soonOverlay.gameObject.AddComponent<Image>();
            ConfigureImage(soonImage, new Color32(0, 0, 0, 110));
            soonImage.raycastTarget = false;
            CreateLabel(soonOverlay, "SoonLabel", "СКОРО", Vector2.zero,
                new Vector2(360f, 80f), 34f, Color.white);
        }

        CreateStarRow(card, stars, new Vector2(0f, -262f));
        CreateLabel(card, "ParHint",
            unlocked ? "3 ЗВЕЗДЫ — ДО " + level.ThreeStarShots + " "
                + ShotsWord(level.ThreeStarShots) : string.Empty,
            new Vector2(0f, -180f), new Vector2(400f, 30f), 17f,
            new Color32(90, 90, 90, 255));
    }

    private void CreateStarRow(Transform parent, int filledStars, Vector2 position)
    {
        RectTransform row = CreateRect(
            "Stars",
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(360f, 80f),
            position);

        float spacing = 108f;
        float startX = -spacing;
        for (int i = 0; i < LevelProgress.MaxStars; i++)
        {
            RectTransform starRect = CreateRect(
                "Star" + (i + 1),
                row,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(72f, 72f),
                new Vector2(startX + spacing * i, 0f));
            Image image = starRect.gameObject.AddComponent<Image>();
            image.sprite = i < filledStars ? starOnSprite : starOffSprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;
        }
    }

    private void OnLevelClicked(int levelId)
    {
        LevelDefinition level = LevelProgress.GetLevel(levelId);
        if (!LevelProgress.IsUnlocked(level.Id))
        {
            return;
        }

        if (!level.CanLoad)
        {
            ShowComingSoon();
            return;
        }

        LevelProgress.SelectLevel(level.Id);
        SceneManager.LoadScene(level.SceneName);
    }

    private GameObject BuildComingSoonOverlay(Transform parent)
    {
        RectTransform overlay = CreateRect(
            "ComingSoonOverlay",
            parent,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero);
        Image overlayImage = overlay.gameObject.AddComponent<Image>();
        ConfigureImage(overlayImage, new Color32(0, 0, 0, 205));

        CreateLabel(overlay, "SoonTitle", "СКОРО", new Vector2(0f, 80f),
            new Vector2(900f, 90f), 54f, Color.white);
        CreateLabel(overlay, "SoonBody", "ЭТОТ УРОВЕНЬ ЕЩЁ В РАЗРАБОТКЕ",
            new Vector2(0f, -10f), new Vector2(1200f, 70f), 32f, Color.white);
        CreateButton(overlay, "SoonBackButton", "ПОНЯТНО",
            new Vector2(480f, 82f), new Vector2(0f, -160f), HideComingSoon, 40f);
        return overlay.gameObject;
    }

    private void ShowComingSoon()
    {
        if (comingSoonOverlay != null)
        {
            comingSoonOverlay.SetActive(true);
        }
    }

    private void HideComingSoon()
    {
        if (comingSoonOverlay != null)
        {
            comingSoonOverlay.SetActive(false);
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
        ConfigureImage(background, Color.white);

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

    private RectTransform CreateRect(
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

    private void ConfigureImage(Image image, Color color)
    {
        image.sprite = uiSprite;
        image.type = Image.Type.Simple;
        image.color = color;
        image.raycastTarget = true;
    }

    private static string ShotsWord(int shots)
    {
        return shots == 1 ? "ВЫСТРЕЛА" : "ВЫСТРЕЛОВ";
    }

    private static Sprite LoadPreview(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath))
        {
            return null;
        }

        Sprite sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite != null)
        {
            return sprite;
        }

        Texture2D texture = Resources.Load<Texture2D>(resourcePath);
        if (texture == null)
        {
            return null;
        }

        return Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f));
    }
}
