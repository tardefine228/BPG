using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Runtime-built map lobby. Levels are laid out as pages of cards (three per
/// page by default) and the pages can be flipped with the arrow buttons, the
/// dots, a drag / swipe, the mouse wheel or the arrow keys. Each card shows the
/// level name, a preview image and earned stars. Later levels stay locked until
/// the previous one is completed.
/// </summary>
[DisallowMultipleComponent]
public sealed class LevelSelectController : MonoBehaviour
{
    private const int CanvasSortingOrder = 10;
    private const float CardWidth = 440f;
    private const float CardHeight = 640f;
    private const float CardSpacing = 520f;
    private const float ViewportHeight = 700f;
    private const float ViewportY = -18f;
    private const float ArrowX = 848f;
    private const float ArrowY = -18f;
    private const float ScrollCooldown = 0.15f;

    [Tooltip("Открыть все уровни сразу — удобно для тестирования новых карт.")]
    [SerializeField] private bool unlockAllLevels;

    [Tooltip("Сколько карточек уровней помещается на одну страницу " +
             "(вёрстка рассчитана на 3 карточки в ряд).")]
    [SerializeField, Min(1)] private int cardsPerPage = 3;

    [Tooltip("Длительность анимации перелистывания страницы, секунды.")]
    [SerializeField, Min(0.05f)] private float slideDuration = 0.32f;

    [Tooltip("Сдвиг мышью или пальцем, при котором страница перелистывается.")]
    [SerializeField, Min(0f)] private float dragFlipDistance = 90f;

    private Sprite uiSprite;
    private Sprite starOnSprite;
    private Sprite starOffSprite;
    private Sprite arrowLeftSprite;
    private Sprite arrowRightSprite;
    private Sprite dotSprite;
    private Sprite dotRingSprite;
    private TMP_FontAsset uiFont;
    private GameObject comingSoonOverlay;
    private LevelSelectPagingSurface pagingSurface;
    private Canvas canvas;
    private RectTransform track;
    private Button previousPageButton;
    private Button nextPageButton;
    private readonly List<Image> pageDots = new List<Image>();
    private float pageWidth = CardSpacing;
    private int pageCount = 1;
    private int currentPage;
    private Coroutine slideRoutine;
    private float dragDistance;
    private float lastScrollTime = -10f;
    private float suppressClickUntil = -10f;

    private void Awake()
    {
        Time.timeScale = 1f;
        LevelProgress.UnlockAllLevels = unlockAllLevels;
        LevelProgress.RestoreSelectedLevel();
        BuildLobby();
    }

    private void OnDestroy()
    {
        if (pagingSurface != null)
        {
            pagingSurface.Dragged -= OnDraggedHorizontally;
            pagingSurface.DragCompleted -= OnDragCompleted;
            pagingSurface.Scrolled -= OnScrolled;
        }

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

        DestroyIcon(arrowLeftSprite);
        DestroyIcon(arrowRightSprite);
        DestroyIcon(dotSprite);
        DestroyIcon(dotRingSprite);
    }

    private void Update()
    {
        if (pageCount <= 1)
        {
            return;
        }

#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.rightArrowKey.wasPressedThisFrame)
        {
            StepPage(1);
        }
        else if (keyboard.leftArrowKey.wasPressedThisFrame)
        {
            StepPage(-1);
        }
#endif
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

        canvas = canvasObject.GetComponent<Canvas>();
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
        arrowRightSprite = MenuSpriteFactory.CreateArrow(true);
        arrowLeftSprite = MenuSpriteFactory.CreateArrow(false);
        dotSprite = MenuSpriteFactory.CreateCircle();
        dotRingSprite = MenuSpriteFactory.CreateRing();

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

        pagingSurface = root.gameObject.AddComponent<LevelSelectPagingSurface>();
        pagingSurface.Dragged += OnDraggedHorizontally;
        pagingSurface.DragCompleted += OnDragCompleted;
        pagingSurface.Scrolled += OnScrolled;

        CreateLabel(root, "Title", "ВЫБОР УРОВНЯ", new Vector2(0f, 436f),
            new Vector2(1100f, 90f), 58f, Color.white);

        LevelDefinition[] levels = LevelProgress.Levels;
        CreateLabel(root, "StarsSummary", "СОБРАНО ЗВЁЗД: " + CountCollectedStars(levels)
            + " / " + (levels.Length * LevelProgress.MaxStars),
            new Vector2(0f, 360f), new Vector2(900f, 44f), 26f,
            new Color32(150, 150, 150, 255));

        RectTransform viewport = CreateRect(
            "Viewport",
            root,
            new Vector2(0f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0f, ViewportHeight),
            new Vector2(0f, ViewportY));
        viewport.gameObject.AddComponent<RectMask2D>();

        track = CreateRect(
            "Pages",
            viewport,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0f, ViewportHeight),
            Vector2.zero);

        pageCount = Mathf.Max(1, Mathf.CeilToInt(levels.Length / (float)cardsPerPage));
        pageWidth = CardSpacing * cardsPerPage;

        for (int i = 0; i < levels.Length; i++)
        {
            CreateLevelCard(track, levels[i], i);
        }

        CreateArrowButton(root, -ArrowX, false);
        CreateArrowButton(root, ArrowX, true);
        BuildPageDots(root);

        if (pageCount > 1)
        {
            CreateLabel(root, "PagingHint",
                "ЛИСТАЙТЕ СТРАНИЦЫ: СТРЕЛКИ, КОЛЕСО ИЛИ СВАЙП",
                new Vector2(0f, -468f), new Vector2(1200f, 36f), 20f,
                new Color32(96, 96, 96, 255));
        }

        comingSoonOverlay = BuildComingSoonOverlay(root);
        comingSoonOverlay.SetActive(false);

        int focusIndex = Mathf.Clamp(IndexOfLevel(LevelProgress.CurrentLevelId), 0,
            Mathf.Max(0, levels.Length - 1));
        SetPageImmediate(focusIndex / cardsPerPage);
    }

    private void CreateLevelCard(Transform parent, LevelDefinition level, int index)
    {
        bool unlocked = LevelProgress.IsUnlocked(level.Id);
        bool isCurrent = level.Id == LevelProgress.CurrentLevelId;
        int stars = LevelProgress.GetStars(level.Id);
        Vector2 position = new Vector2(CardX(index), 0f);

        if (isCurrent)
        {
            RectTransform highlight = CreateRect(
                "CurrentLevel_" + level.Id,
                parent,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(CardWidth + 18f, CardHeight + 18f),
                position);
            Image highlightImage = highlight.gameObject.AddComponent<Image>();
            ConfigureImage(highlightImage, new Color32(230, 155, 82, 255));
            highlightImage.raycastTarget = false;
        }

        RectTransform card = CreateRect(
            "LevelCard_" + level.Id,
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(CardWidth, CardHeight),
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

    // ------------------------------------------------------------- pages ----
    private float CardX(int index)
    {
        int page = index / cardsPerPage;
        int firstOnPage = page * cardsPerPage;
        int countOnPage = Mathf.Min(cardsPerPage, LevelProgress.Levels.Length - firstOnPage);
        float slot = index - firstOnPage - (countOnPage - 1) * 0.5f;
        return page * pageWidth + slot * CardSpacing;
    }

    private void CreateArrowButton(Transform parent, float x, bool pointsRight)
    {
        RectTransform rect = CreateRect(
            pointsRight ? "NextPageButton" : "PreviousPageButton",
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(120f, 220f),
            new Vector2(x, ArrowY));

        Image background = rect.gameObject.AddComponent<Image>();
        ConfigureImage(background, new Color32(38, 38, 42, 255));

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = MenuColorBlock.Create();
        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        int step = pointsRight ? 1 : -1;
        button.onClick.AddListener(() => StepPage(step));

        RectTransform icon = CreateRect(
            "Icon",
            rect,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(96f, 96f),
            Vector2.zero);
        Image iconImage = icon.gameObject.AddComponent<Image>();
        ConfigureImage(iconImage, Color.white);
        iconImage.sprite = pointsRight ? arrowRightSprite : arrowLeftSprite;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        if (pointsRight)
        {
            nextPageButton = button;
        }
        else
        {
            previousPageButton = button;
        }
    }

    private void BuildPageDots(Transform parent)
    {
        if (pageCount <= 1)
        {
            return;
        }

        RectTransform row = CreateRect(
            "PageDots",
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(pageCount * 58f, 44f),
            new Vector2(0f, -420f));

        for (int i = 0; i < pageCount; i++)
        {
            RectTransform dot = CreateRect(
                "Dot" + i,
                row,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(34f, 34f),
                new Vector2((i - (pageCount - 1) * 0.5f) * 58f, 0f));

            Image image = dot.gameObject.AddComponent<Image>();
            ConfigureImage(image, Color.white);
            image.sprite = dotRingSprite;
            image.preserveAspect = true;

            Button button = dot.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = MenuColorBlock.Create();
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            int page = i;
            button.onClick.AddListener(() => SetPage(page));

            pageDots.Add(image);
        }
    }

    private void StepPage(int delta)
    {
        SetPage(currentPage + delta);
    }

    private void SetPage(int page)
    {
        int clamped = Mathf.Clamp(page, 0, Mathf.Max(0, pageCount - 1));
        currentPage = clamped;
        RefreshNavigation();

        float target = -currentPage * pageWidth;
        if (track == null)
        {
            return;
        }

        if (slideRoutine != null)
        {
            StopCoroutine(slideRoutine);
            slideRoutine = null;
        }

        if (Mathf.Abs(track.anchoredPosition.x - target) < 0.5f)
        {
            track.anchoredPosition = new Vector2(target, track.anchoredPosition.y);
            return;
        }

        slideRoutine = StartCoroutine(SlideRoutine(target));
    }

    private void SetPageImmediate(int page)
    {
        currentPage = Mathf.Clamp(page, 0, Mathf.Max(0, pageCount - 1));
        if (track != null)
        {
            track.anchoredPosition = new Vector2(-currentPage * pageWidth, track.anchoredPosition.y);
        }

        RefreshNavigation();
    }

    private IEnumerator SlideRoutine(float targetX)
    {
        float startX = track.anchoredPosition.x;
        float y = track.anchoredPosition.y;
        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, slideDuration);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);
            track.anchoredPosition = new Vector2(Mathf.Lerp(startX, targetX, eased), y);
            yield return null;
        }

        track.anchoredPosition = new Vector2(targetX, y);
        slideRoutine = null;
    }

    private void RefreshNavigation()
    {
        // An arrow is only on screen while paging that way is actually possible:
        // on the first page there is no left arrow, after flipping there is.
        if (previousPageButton != null)
        {
            bool canGoBack = pageCount > 1 && currentPage > 0;
            previousPageButton.interactable = canGoBack;
            previousPageButton.gameObject.SetActive(canGoBack);
        }

        if (nextPageButton != null)
        {
            bool canGoForward = pageCount > 1 && currentPage < pageCount - 1;
            nextPageButton.interactable = canGoForward;
            nextPageButton.gameObject.SetActive(canGoForward);
        }

        for (int i = 0; i < pageDots.Count; i++)
        {
            Image dot = pageDots[i];
            if (dot == null)
            {
                continue;
            }

            bool active = i == currentPage;
            dot.sprite = active ? dotSprite : dotRingSprite;
            dot.color = active ? new Color32(230, 155, 82, 255) : new Color32(110, 110, 110, 255);
        }
    }

    private void OnDraggedHorizontally(float deltaX)
    {
        if (pageCount <= 1 || track == null || Mathf.Approximately(deltaX, 0f))
        {
            return;
        }

        if (slideRoutine != null)
        {
            StopCoroutine(slideRoutine);
            slideRoutine = null;
        }

        dragDistance += deltaX;
        float scale = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
        float min = -(pageCount - 1) * pageWidth;
        float x = Mathf.Clamp(track.anchoredPosition.x + deltaX / scale, min, 0f);
        track.anchoredPosition = new Vector2(x, track.anchoredPosition.y);
    }

    private void OnDragCompleted()
    {
        if (pageCount <= 1 || track == null)
        {
            dragDistance = 0f;
            return;
        }

        float moved = dragDistance;
        dragDistance = 0f;

        if (Mathf.Abs(moved) >= 12f)
        {
            // A drag that ends above a card must not load the level.
            suppressClickUntil = Time.unscaledTime + 0.25f;
        }

        int page = currentPage;
        if (Mathf.Abs(moved) >= dragFlipDistance)
        {
            page = currentPage + (moved > 0f ? -1 : 1);
        }
        else
        {
            page = Mathf.RoundToInt(-track.anchoredPosition.x / pageWidth);
        }

        SetPage(page);
    }

    private void OnScrolled(float delta)
    {
        if (pageCount <= 1 || Mathf.Abs(delta) < 0.01f)
        {
            return;
        }

        if (Time.unscaledTime - lastScrollTime < ScrollCooldown)
        {
            return;
        }

        lastScrollTime = Time.unscaledTime;
        StepPage(delta > 0f ? -1 : 1);
    }

    private static int IndexOfLevel(int levelId)
    {
        LevelDefinition[] levels = LevelProgress.Levels;
        for (int i = 0; i < levels.Length; i++)
        {
            if (levels[i].Id == levelId)
            {
                return i;
            }
        }

        return 0;
    }

    private static int CountCollectedStars(LevelDefinition[] levels)
    {
        int total = 0;
        for (int i = 0; i < levels.Length; i++)
        {
            total += LevelProgress.GetStars(levels[i].Id);
        }

        return total;
    }

    // ------------------------------------------------------------ cards ----
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
        if (Time.unscaledTime < suppressClickUntil)
        {
            return;
        }

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

    // ------------------------------------------------------------- ui ------
    private Button CreateButton(
        Transform parent,
        string objectName,
        string label,
        Vector2 size,
        Vector2 position,
        UnityEngine.Events.UnityAction onClick,
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

    private static void DestroyIcon(Sprite sprite)
    {
        if (sprite == null)
        {
            return;
        }

        Destroy(sprite.texture);
        Destroy(sprite);
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
