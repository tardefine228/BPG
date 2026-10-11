using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Level-selection panel inside MainMenu. Progress is refreshed every time it opens.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
public sealed class LevelSelectController : MonoBehaviour
{
    [SerializeField] private MenuManager menuManager;
    private RectTransform cardsRoot;
    private bool built;
    private readonly Dictionary<string, Sprite> previews = new Dictionary<string, Sprite>();
    private readonly List<Sprite> generatedPreviews = new List<Sprite>();

    private Sprite uiSprite;
    private Sprite starOnSprite;
    private Sprite starOffSprite;
    private TMP_FontAsset uiFont;
    private GameObject comingSoonOverlay;

    private void Awake()
    {
        LevelProgress.RestoreSelectedLevel();
        BuildLobby();
    }

    public void Initialize(MenuManager owner)
    {
        menuManager = owner;
        BuildLobby();
        RefreshCards();
    }

    private void OnEnable()
    {
        RefreshCards();
        if (comingSoonOverlay != null)
            comingSoonOverlay.SetActive(false);
    }

    private void OnDestroy()
    {
        foreach (Sprite preview in generatedPreviews)
            Destroy(preview);
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
        if (built)
            return;
        built = true;
        if (menuManager == null)
            menuManager = FindAnyObjectByType<MenuManager>();

        uiSprite = Sprite.Create(
            Texture2D.whiteTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f));
        uiFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        starOnSprite = StarSpriteFactory.Create(true);
        starOffSprite = StarSpriteFactory.Create(false);

        RectTransform root = CreateRect(
            "Root",
            transform,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero);

        Image background = root.gameObject.AddComponent<Image>();
        ConfigureImage(background, new Color32(0, 0, 0, 255));

        CreateLabel(root, "Title", "ВЫБОР УРОВНЯ", new Vector2(0f, 430f),
            new Vector2(1100f, 90f), 58f, Color.white);

        cardsRoot = CreateRect("LevelCards", root, Vector2.zero, Vector2.one,
            new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        MenuButtonStyle.Default.CreateButton(root, "BackButton", "НАЗАД", Vector2.one,
            new Vector2(180f, 76f), new Vector2(-38f, -38f), menuManager.CloseLevels, 30f);

        comingSoonOverlay = BuildComingSoonOverlay(root);
        comingSoonOverlay.SetActive(false);
    }

    private void RefreshCards()
    {
        if (cardsRoot == null)
            return;
        foreach (Transform child in cardsRoot)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        LevelDefinition[] levels = LevelProgress.Levels;
        float spacing = 500f;
        float startX = -spacing * (levels.Length - 1) * 0.5f;
        for (int i = 0; i < levels.Length; i++)
            CreateLevelCard(cardsRoot, levels[i], new Vector2(startX + spacing * i, -20f));
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
        ConfigureImage(cardImage, unlocked ? Color.white : (Color)new Color32(188, 188, 188, 255));

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
        card.gameObject.AddComponent<MenuControlFeedback>();

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
        previewImage.color = unlocked ? Color.white : (Color)new Color32(140, 140, 140, 255);
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

        AudioManager.Instance?.PlayUiClick();
        LevelProgress.SelectLevel(level.Id);
        SceneTransition.Load(level.SceneName);
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

    public bool DismissComingSoon()
    {
        bool open = comingSoonOverlay != null && comingSoonOverlay.activeSelf;
        HideComingSoon();
        return open;
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

    private Sprite LoadPreview(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath))
        {
            return null;
        }
        if (previews.TryGetValue(resourcePath, out Sprite cached))
            return cached;

        Sprite sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite != null)
        {
            previews[resourcePath] = sprite;
            return sprite;
        }

        Texture2D texture = Resources.Load<Texture2D>(resourcePath);
        if (texture == null)
        {
            return null;
        }

        sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f));
        generatedPreviews.Add(sprite);
        previews[resourcePath] = sprite;
        return sprite;
    }
}
