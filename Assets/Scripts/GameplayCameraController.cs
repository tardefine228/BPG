using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[RequireComponent(typeof(Camera))]
public class GameplayCameraController : MonoBehaviour
{
    [Header("Scene")]
    public SlingshotController slingshot;
    public Transform launcherFocus;
    public Transform targetFocus;
    public SpriteRenderer ground;
    public SpriteRenderer background;
    public Vector2 mapMin = new Vector2(-85f, -7.5f);
    public Vector2 mapMax = new Vector2(95f, 62.5f);
    public float overviewPadding = 4f;

    [Header("Manual zoom")]
    public float launcherSize = 8f;
    public float impactSize = 12f;
    public Vector2 launcherOffset = new Vector2(2f, 2f);
    public float transitionDuration = 0.5f;

    [Header("Future upgrade menu")]
    public UnityEvent onLauncherFocused = new UnityEvent();
    public bool showControls = true;
    public bool IsBusy => preparingShot || viewTransition != null;

    private enum ViewMode { Overview, Launcher, Target }
    private ViewMode mode;
    private Camera view;
    private Coroutine viewTransition;
    private bool preparingShot;
    private float lastAspect;
    private float groundSurface;
    private GameObject controlsCanvas;
    private RectTransform controlsRect;
    private Button launcherButton;
    private Button overviewButton;
    private Button targetButton;

    private bool CanNavigate => !preparingShot && slingshot != null
        && slingshot.isActiveAndEnabled && !slingshot.IsDragging && Time.timeScale > 0f;
    private Vector2 OverviewPosition => (mapMin + mapMax) * 0.5f;
    private float OverviewSize => Mathf.Max((mapMax.y - mapMin.y) * 0.5f,
        (mapMax.x - mapMin.x) / (2f * Mathf.Max(0.1f, view.aspect))) + overviewPadding;
    private Vector2 LauncherPosition => (Vector2)launcherFocus.position + launcherOffset;

    private void Awake()
    {
        view = GetComponent<Camera>();
        view.orthographic = true;
        if (ground != null)
            groundSurface = ground.bounds.max.y;
    }

    private void Start()
    {
        FitBackdrop();
        SetView(OverviewPosition, OverviewSize);
        lastAspect = view.aspect;
        BuildControls();
    }

    private void LateUpdate()
    {
        if (!Mathf.Approximately(lastAspect, view.aspect))
        {
            FitBackdrop();
            if (!IsBusy)
            {
                GetSelectedView(out Vector2 position, out float size);
                SetView(position, size);
            }
            lastAspect = view.aspect;
        }
        if (controlsCanvas == null)
            return;
        controlsCanvas.SetActive(showControls && slingshot != null
            && slingshot.isActiveAndEnabled && Time.timeScale > 0f);
        launcherButton.interactable = CanNavigate && mode != ViewMode.Launcher;
        overviewButton.interactable = CanNavigate && mode != ViewMode.Overview;
        targetButton.interactable = CanNavigate && mode != ViewMode.Target && targetFocus != null;
    }

    // Every shot starts from the overview. Flight and impact never move the camera.
    public IEnumerator PrepareForShot()
    {
        StopViewTransition();
        preparingShot = true;
        mode = ViewMode.Overview;
        if (Vector2.Distance(transform.position, OverviewPosition) > 0.01f
            || Mathf.Abs(view.orthographicSize - OverviewSize) > 0.01f)
            yield return MoveView(OverviewPosition, OverviewSize, transitionDuration);
        SetView(OverviewPosition, OverviewSize);
        preparingShot = false;
    }

    public void FocusLauncher() => SelectView(ViewMode.Launcher);
    public void FocusTarget() => SelectView(ViewMode.Target);
    public void ShowOverview() => SelectView(ViewMode.Overview);

    private void SelectView(ViewMode selected)
    {
        if (!CanNavigate || (selected == ViewMode.Target && targetFocus == null))
            return;
        StopViewTransition();
        mode = selected;
        viewTransition = StartCoroutine(ChangeView());
    }

    private IEnumerator ChangeView()
    {
        GetSelectedView(out Vector2 position, out float size);
        yield return MoveView(position, size, transitionDuration);
        GetSelectedView(out position, out size);
        SetView(position, size);
        viewTransition = null;
        if (mode == ViewMode.Launcher)
            onLauncherFocused.Invoke();
    }

    private void GetSelectedView(out Vector2 position, out float size)
    {
        position = OverviewPosition;
        size = OverviewSize;
        if (mode == ViewMode.Launcher)
        {
            position = LauncherPosition;
            size = launcherSize;
        }
        else if (mode == ViewMode.Target && targetFocus != null)
        {
            // Keep the building area framed even after its debris scatters.
            position = (Vector2)targetFocus.position + Vector2.up * 3f;
            size = impactSize;
        }
    }

    private void StopViewTransition()
    {
        if (viewTransition != null)
            StopCoroutine(viewTransition);
        viewTransition = null;
    }

    private IEnumerator MoveView(Vector2 position, float size, float duration)
    {
        Vector2 from = transform.position;
        float fromSize = view.orthographicSize;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            SetView(Vector2.Lerp(from, position, t), Mathf.Lerp(fromSize, size, t));
            yield return null;
        }
        SetView(position, size);
    }

    private void SetView(Vector2 position, float size)
    {
        transform.position = new Vector3(position.x, position.y, transform.position.z);
        view.orthographicSize = Mathf.Max(0.1f, size);
    }

    private void FitBackdrop()
    {
        // Keep the play area small, with extra ground beyond the visible screen edges.
        float halfWidth = OverviewSize * view.aspect;
        float left = Mathf.Min(mapMin.x, OverviewPosition.x - halfWidth);
        float right = Mathf.Max(mapMax.x, OverviewPosition.x + halfWidth);
        if (launcherFocus != null)
        {
            left = Mathf.Min(left, LauncherPosition.x - launcherSize * view.aspect);
            right = Mathf.Max(right, LauncherPosition.x + launcherSize * view.aspect);
        }
        if (targetFocus != null)
        {
            left = Mathf.Min(left, targetFocus.position.x - impactSize * view.aspect);
            right = Mathf.Max(right, targetFocus.position.x + impactSize * view.aspect);
        }
        left -= 8f;
        right += 8f;
        float bottom = Mathf.Min(groundSurface - 80f, OverviewPosition.y - OverviewSize - 8f);
        float top = OverviewPosition.y + OverviewSize + 8f;
        if (ground != null && ground.sprite != null)
        {
            ground.transform.position = new Vector3((left + right) * 0.5f,
                (groundSurface + bottom) * 0.5f, ground.transform.position.z);
            Vector3 spriteSize = ground.sprite.bounds.size;
            ground.transform.localScale = new Vector3((right - left) / spriteSize.x,
                (groundSurface - bottom) / spriteSize.y, 1f);
        }
        if (background != null)
        {
            background.transform.position = new Vector3((left + right) * 0.5f,
                (bottom + top) * 0.5f, background.transform.position.z);
            background.size = new Vector2(Mathf.Max(240f, right - left), Mathf.Max(180f, top - bottom));
        }
    }

    private void BuildControls()
    {
        controlsCanvas = new GameObject("GameplayCameraCanvas", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        controlsCanvas.layer = 5;
        Canvas canvas = controlsCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = controlsCanvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject group = new GameObject("CameraControls", typeof(RectTransform));
        group.layer = 5;
        controlsRect = group.GetComponent<RectTransform>();
        controlsRect.SetParent(controlsCanvas.transform, false);
        controlsRect.anchorMin = controlsRect.anchorMax = controlsRect.pivot = new Vector2(0f, 1f);
        controlsRect.anchoredPosition = new Vector2(38f, -38f);
        controlsRect.sizeDelta = new Vector2(774f, 120f);
        MenuButtonStyle style = MenuButtonStyle.Default;
        Vector2 anchor = new Vector2(0f, 1f);
        Vector2 size = new Vector2(250f, 76f);
        launcherButton = style.CreateButton(controlsRect, "LauncherViewButton", "КАТАПУЛЬТА",
            anchor, size, Vector2.zero, FocusLauncher, 30f);
        overviewButton = style.CreateButton(controlsRect, "OverviewButton", "ВСЯ КАРТА",
            anchor, size, new Vector2(262f, 0f), ShowOverview, 30f);
        targetButton = style.CreateButton(controlsRect, "TargetViewButton", "ЦЕЛЬ",
            anchor, size, new Vector2(524f, 0f), FocusTarget, 30f);

        GameObject hint = new GameObject("AimHint", typeof(RectTransform));
        hint.transform.SetParent(controlsRect, false);
        RectTransform hintRect = hint.GetComponent<RectTransform>();
        hintRect.anchorMin = hintRect.anchorMax = hintRect.pivot = anchor;
        hintRect.anchoredPosition = new Vector2(0f, -84f);
        hintRect.sizeDelta = new Vector2(774f, 36f);
        TextMeshProUGUI text = hint.AddComponent<TextMeshProUGUI>();
        text.font = style.font;
        text.fontSize = 24f;
        text.color = style.textColor;
        text.alignment = TextAlignmentOptions.Left;
        text.text = "Выстрел: потяните снаряд и отпустите";
        text.raycastTarget = false;
    }

    public bool IsPointerOverControls(Vector2 screenPosition) => controlsCanvas != null
        && controlsCanvas.activeInHierarchy
        && RectTransformUtility.RectangleContainsScreenPoint(controlsRect, screenPosition);

    private void OnDisable()
    {
        StopAllCoroutines();
        viewTransition = null;
        preparingShot = false;
        if (controlsCanvas != null)
            controlsCanvas.SetActive(false);
    }

    private void OnDestroy()
    {
        if (controlsCanvas != null)
            Destroy(controlsCanvas);
    }
}
