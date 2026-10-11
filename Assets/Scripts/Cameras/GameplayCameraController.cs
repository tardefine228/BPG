using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[RequireComponent(typeof(Camera))]
public class GameplayCameraController : MonoBehaviour
{
    [Header("Объекты сцены")]
    public SlingshotController slingshot;
    public Transform launcherFocus;
    public Transform targetFocus;
    public SpriteRenderer ground;
    public SpriteRenderer background;
    public Vector2 mapMin = new Vector2(-85f, -7.5f);
    public Vector2 mapMax = new Vector2(95f, 62.5f);
    public float overviewPadding = 4f;

    [Header("Ручное приближение")]
    public float launcherSize = 8f;
    public float impactSize = 12f;
    public Vector2 launcherOffset = new Vector2(2f, 2f);
    public float transitionDuration = 0.5f;

    [Header("Камера выстрела")]
    public float shotLauncherSize = 20f;
    public float preShotHold = 1f;
    public float followSize = 20f;
    public float followSharpness = 12f;
    public float followLookAhead = 0.16f;
    public float groundHold = 1f;
    public float buildingHold = 2f;
    public float returnDuration = 0.7f;
    [Tooltip("Плавное перемещение к месту попадания без дополнительного увеличения.")]
    [Min(0.1f)] public float impactTransitionDuration = 0.75f;

    [Header("Управление камерой")]
    public UnityEvent onLauncherFocused = new UnityEvent();
    public bool showControls = true;
    public bool IsBusy => shotSequence || viewTransition != null;

    private enum ViewMode { Overview, Launcher, Target }
    private ViewMode mode;
    private Camera view;
    private Coroutine viewTransition;
    private bool shotSequence;
    private bool impactRecorded;
    private bool buildingImpact;
    private Vector2 impactFocus;
    private Rigidbody2D trackedProjectile;
    private Coroutine cinematic;
    private float lastAspect;
    private float groundSurface;
    [SerializeField] private GameObject controlsCanvas;
    [SerializeField] private RectTransform controlsRect;
    [SerializeField] private Button launcherButton;
    [SerializeField] private Button overviewButton;
    [SerializeField] private Button targetButton;

    private bool CanNavigate => !shotSequence && !SceneTransition.IsLoading && slingshot != null
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
    }

    private void LateUpdate()
    {
        if (trackedProjectile != null && !impactRecorded && Time.timeScale > 0f)
        {
            Vector2 destination = trackedProjectile.position
                + trackedProjectile.linearVelocity * followLookAhead + Vector2.up * 2f;
            float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            SetView(Vector2.Lerp(transform.position, destination, t),
                Mathf.Lerp(view.orthographicSize, followSize, t));
        }
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

    public IEnumerator PrepareForShot()
    {
        StopViewTransition();
        shotSequence = true;
        impactRecorded = false;
        mode = ViewMode.Overview;
        yield return MoveView(LauncherPosition, shotLauncherSize, transitionDuration);
        yield return new WaitForSeconds(preShotHold);
    }

    public void FollowShot(Rigidbody2D body)
    {
        trackedProjectile = body;
        if (cinematic != null)
            StopCoroutine(cinematic);
        cinematic = StartCoroutine(TrackShot());
    }

    public void RecordImpact(Vector2 point, Collider2D collider)
    {
        if (!shotSequence || impactRecorded)
            return;
        impactRecorded = true;
        buildingImpact = collider != null && (collider.GetComponentInParent<LevelTarget>() != null
            || (targetFocus != null && collider.transform.IsChildOf(targetFocus)));
        impactFocus = buildingImpact && targetFocus != null && collider.transform.IsChildOf(targetFocus)
            ? (Vector2)targetFocus.position + Vector2.up * 3f
            : point + Vector2.up * (impactSize * 0.3f);
    }

    private IEnumerator TrackShot()
    {
        float deadline = Time.time + 7f;
        while (!impactRecorded && trackedProjectile != null && Time.time < deadline)
            yield return null;
        if (!impactRecorded)
        {
            buildingImpact = false;
            impactFocus = trackedProjectile != null
                ? trackedProjectile.position + Vector2.up * (impactSize * 0.3f)
                : (Vector2)transform.position;
        }
        trackedProjectile = null;
        yield return MoveView(impactFocus, view.orthographicSize, impactTransitionDuration);
        yield return new WaitForSeconds(buildingImpact ? buildingHold : groundHold);
        yield return MoveView(OverviewPosition, OverviewSize, returnDuration);
        shotSequence = false;
        cinematic = null;
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
            // Удерживает центр постройки в кадре даже после разлёта обломков.
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
        ExpandBackdrop(position, view.orthographicSize);
    }

    private void ExpandBackdrop(Vector2 position, float size)
    {
        // Продлевает землю и фон, чтобы при полёте за границы карты не появились пустоты.
        float left = position.x - size * view.aspect - 8f;
        float right = position.x + size * view.aspect + 8f;
        float bottom = position.y - size - 8f;
        float top = position.y + size + 8f;
        if (ground != null && ground.sprite != null)
        {
            Bounds bounds = ground.bounds;
            left = Mathf.Min(left, bounds.min.x);
            right = Mathf.Max(right, bounds.max.x);
            float groundBottom = Mathf.Min(bottom, bounds.min.y);
            ground.transform.position = new Vector3((left + right) * 0.5f,
                (groundSurface + groundBottom) * 0.5f, ground.transform.position.z);
            Vector3 spriteSize = ground.sprite.bounds.size;
            ground.transform.localScale = new Vector3((right - left) / spriteSize.x,
                (groundSurface - groundBottom) / spriteSize.y, 1f);
        }
        if (background != null)
        {
            Bounds bounds = background.bounds;
            left = Mathf.Min(left, bounds.min.x);
            right = Mathf.Max(right, bounds.max.x);
            bottom = Mathf.Min(bottom, bounds.min.y);
            top = Mathf.Max(top, bounds.max.y);
            background.transform.position = new Vector3((left + right) * 0.5f,
                (bottom + top) * 0.5f, background.transform.position.z);
            background.size = new Vector2(right - left, top - bottom);
        }
    }

    private void FitBackdrop()
    {
        // Добавляет запас земли за видимыми краями экрана.
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

    public bool IsPointerOverControls(Vector2 screenPosition) => controlsCanvas != null
        && controlsCanvas.activeInHierarchy
        && RectTransformUtility.RectangleContainsScreenPoint(controlsRect, screenPosition);

    private void OnDisable()
    {
        StopAllCoroutines();
        viewTransition = null;
        shotSequence = false;
        trackedProjectile = null;
        cinematic = null;
        if (controlsCanvas != null)
            controlsCanvas.SetActive(false);
    }
}
