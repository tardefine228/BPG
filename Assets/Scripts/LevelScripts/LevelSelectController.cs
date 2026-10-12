using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Управляет готовыми карточками и перелистыванием; интерфейс редактируется в сцене.
[DisallowMultipleComponent]
public sealed class LevelSelectController : MonoBehaviour
{
    private const float ScrollCooldown = 0.15f;
    [SerializeField] private bool unlockAllLevels;
    [SerializeField, Min(0.05f)] private float slideDuration = 0.32f;
    [SerializeField, Min(0f)] private float dragFlipDistance = 90f;
    [SerializeField] private MenuManager menuManager;
    [SerializeField] private TMP_Text starsSummary;
    [SerializeField] private LevelCardView[] cards;
    [SerializeField] private Sprite starOnSprite;
    [SerializeField] private Sprite starOffSprite;
    [SerializeField] private Sprite dotCurrentSprite;
    [SerializeField] private Sprite dotOtherSprite;
    [SerializeField] private GameObject comingSoonOverlay;
    [SerializeField] private LevelSelectPagingSurface pagingSurface;
    [SerializeField] private RectTransform track;
    [SerializeField] private Button previousPageButton;
    [SerializeField] private Button nextPageButton;
    [SerializeField] private TMP_Text pageLabel;
    [SerializeField] private List<Image> pageDots = new List<Image>();

    private Canvas canvas;
    private float pageWidth;
    private int pageCount;
    private int currentPage;
    private Coroutine slideRoutine;
    private float dragDistance;
    private float lastScrollTime = -10f;
    private float suppressClickUntil = -10f;

    private void Awake()
    {
        canvas = GetComponentInParent<Canvas>();
        pageWidth = ((RectTransform)track.parent).rect.width;
        pageCount = Mathf.Max(1, pageDots.Count);
        pagingSurface.Dragged += OnDraggedHorizontally;
        pagingSurface.DragCompleted += OnDragCompleted;
        pagingSurface.Scrolled += OnScrolled;
    }

    public void Initialize(MenuManager owner)
    {
        menuManager = owner;
        LevelProgress.UnlockAllLevels = unlockAllLevels;
        LevelProgress.RestoreSelectedLevel();
        // Панель может быть неактивна при запуске главного меню.
        if (pageWidth == 0f)
        {
            pageWidth = ((RectTransform)track.parent).rect.width;
            pageCount = Mathf.Max(1, pageDots.Count);
        }
        RefreshCards();
    }

    private void OnEnable()
    {
        LevelProgress.UnlockAllLevels = unlockAllLevels;
        LevelProgress.RestoreSelectedLevel();
        RefreshCards();
        HideComingSoon();
    }

    private void OnDestroy()
    {
        if (pagingSurface == null) return;
        pagingSurface.Dragged -= OnDraggedHorizontally;
        pagingSurface.DragCompleted -= OnDragCompleted;
        pagingSurface.Scrolled -= OnScrolled;
    }

    private void RefreshCards()
    {
        int selectedPage = 0;
        foreach (LevelCardView card in cards)
        {
            card.Refresh(starOnSprite, starOffSprite);
            if (card.LevelId == LevelProgress.CurrentLevelId)
                selectedPage = card.PageIndex;
        }
        starsSummary.text = "СОБРАНО ЗВЁЗД: " + LevelProgress.TotalStars
            + " / " + LevelProgress.Levels.Length * LevelProgress.MaxStars;
        SetPageImmediate(selectedPage);
        dragDistance = 0f;
        suppressClickUntil = -10f;
        lastScrollTime = -10f;
    }
    private void OnDisable()
    {
        if (slideRoutine != null)
            StopCoroutine(slideRoutine);
        slideRoutine = null;
        dragDistance = 0f;
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (SceneTransition.IsLoading || (comingSoonOverlay != null && comingSoonOverlay.activeSelf))
            return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (pageCount <= 1)
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

    public void ReturnToMainMenu()
    {
        AudioManager.Instance?.PlayUiClose();
        menuManager.CloseLevels();
    }

    private string PageLabelText() => "СТРАНИЦА " + (currentPage + 1) + " / " + pageCount;

    public void StepPage(int delta)
    {
        SetPage(currentPage + delta);
    }

    public void SetPage(int page)
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
        // На краях списка скрывается стрелка, которая ведёт за пределы страниц.
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
            ApplyDotState(pageDots[i], i == currentPage);
        }

        if (pageLabel != null)
        {
            pageLabel.text = PageLabelText();
        }
    }

    private void ApplyDotState(Image dot, bool isCurrent)
    {
        if (dot == null)
        {
            return;
        }

        // Цвет запечён в спрайт, поэтому здесь только подмена картинки —
        // точке не нужен ни тинт, ни перекраска кнопкой.
        dot.sprite = isCurrent ? dotCurrentSprite : dotOtherSprite;
        dot.color = Color.white;
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
            // Завершение свайпа над карточкой не должно запускать уровень.
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

    public void OnLevelClicked(int levelId)
    {
        if (SceneTransition.IsLoading || Time.unscaledTime < suppressClickUntil)
        {
            return;
        }

        LevelDefinition level = LevelProgress.GetLevel(levelId);
        // Закрытая кнопка недоступна, но проверка остаётся на случай внешнего вызова:
        // уровни 3–5 открываются только запасом звёзд.
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

    private void ShowComingSoon()
    {
        if (comingSoonOverlay != null)
        {
            comingSoonOverlay.SetActive(true);
        }
    }

    public void HideComingSoon()
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
}
