using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Camera))]
public class GameplayCameraController : MonoBehaviour
{
    [Header("Scene")]
    public SlingshotController slingshot;
    public Transform launcherFocus;
    public Transform targetFocus;
    public Vector2 mapMin = new Vector2(-170f, -15f);
    public Vector2 mapMax = new Vector2(190f, 125f);
    public float overviewPadding = 8f;

    [Header("Zoom")]
    public float launcherSize = 8f;
    public float followSize = 16f;
    public float impactSize = 12f;
    public Vector2 launcherOffset = new Vector2(2f, 2f);

    [Header("Shot timing (seconds)")]
    public float launchZoomDuration = 0.65f;
    public float preLaunchHold = 0.12f;
    public float impactLeadTime = 0.85f;
    public float targetTransitionDuration = 0.22f;
    public float impactHoldDuration = 2.5f;
    public float returnDuration = 1.1f;
    public float maxFlightDuration = 7f;
    public float followSharpness = 15f;
    public float followLookAhead = 0.15f;

    [Header("Future upgrade menu")]
    public UnityEvent onLauncherFocused = new UnityEvent();
    public bool showControls = true;
    public bool IsBusy { get; private set; }
    private bool CanNavigate => !IsBusy && slingshot != null
        && slingshot.isActiveAndEnabled && !slingshot.IsDragging && Time.timeScale > 0f;

    private Camera view;
    private Coroutine viewTransition;
    private bool observedImpact;
    private Vector2 actualImpact;
    private bool atLauncher;
    private float lastAspect;

    private Vector2 OverviewPosition => (mapMin + mapMax) * 0.5f;
    private float OverviewSize => Mathf.Max((mapMax.y - mapMin.y) * 0.5f,
        (mapMax.x - mapMin.x) / (2f * Mathf.Max(0.1f, view.aspect))) + overviewPadding;
    private Vector2 LauncherPosition => (Vector2)launcherFocus.position + launcherOffset;

    private void Awake()
    {
        view = GetComponent<Camera>();
        view.orthographic = true;
    }

    private void OnEnable()
    {
        if (slingshot == null)
            return;
        slingshot.ShotLaunched += OnShotLaunched;
        slingshot.ProjectileImpacted += OnProjectileImpacted;
    }

    private void Start()
    {
        SetView(OverviewPosition, OverviewSize);
        lastAspect = view.aspect;
    }

    private void LateUpdate()
    {
        if (!IsBusy && !atLauncher && viewTransition == null
            && !Mathf.Approximately(lastAspect, view.aspect))
            SetView(OverviewPosition, OverviewSize);
        lastAspect = view.aspect;
    }

    public IEnumerator PrepareForShot()
    {
        StopViewTransition();
        IsBusy = true;
        observedImpact = false;
        yield return MoveView(LauncherPosition, launcherSize, launchZoomDuration);
        yield return new WaitForSeconds(preLaunchHold);
    }

    private void OnShotLaunched(Rigidbody2D projectile)
    {
        StartCoroutine(WatchShot(projectile));
    }

    private void OnProjectileImpacted(Vector2 point)
    {
        actualImpact = point;
        observedImpact = true;
    }

    private IEnumerator WatchShot(Rigidbody2D projectile)
    {
        IsBusy = true;
        bool predicted = slingshot.TryPredictImpact(projectile.position,
            projectile.linearVelocity, projectile.gravityScale,
            out Vector2 predictedPoint, out float flightTime, out Collider2D hit);
        Vector2 impactFocus = predictedPoint + Vector2.up * 3f;
        float targetSize = impactSize;

        // Frame the whole target group when the trajectory actually hits it.
        if (predicted && targetFocus != null && hit.transform.IsChildOf(targetFocus))
        {
            Bounds bounds = new Bounds(targetFocus.position, Vector3.zero);
            foreach (Renderer renderer in targetFocus.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(renderer.bounds);
            impactFocus = (Vector2)bounds.center + new Vector2(-4f, 2f);
            targetSize = Mathf.Max(impactSize, bounds.extents.y + 3f,
                (bounds.extents.x + 6f) / Mathf.Max(0.1f, view.aspect));
        }

        float started = Time.time;
        float followDuration = predicted
            ? Mathf.Max(0f, flightTime - impactLeadTime) : maxFlightDuration;
        while (projectile != null && !observedImpact && Time.time - started < followDuration)
        {
            float blend = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            Vector2 focus = projectile.position + projectile.linearVelocity * followLookAhead;
            SetView(Vector2.Lerp(transform.position, focus, blend),
                Mathf.Lerp(view.orthographicSize, followSize, blend));
            yield return null;
        }

        if (observedImpact)
            impactFocus = actualImpact + Vector2.up * 3f;
        if (predicted || observedImpact)
            yield return MoveView(impactFocus, targetSize, targetTransitionDuration);

        // A moving target may invalidate the initial prediction; never lock the next shot.
        float deadline = predicted ? Mathf.Min(maxFlightDuration, flightTime + 1.5f)
            : maxFlightDuration;
        while (projectile != null && !observedImpact && Time.time - started < deadline)
            yield return null;

        if (observedImpact && Vector2.Distance(actualImpact, transform.position)
            > view.orthographicSize)
            yield return MoveView(actualImpact + Vector2.up * 3f,
                impactSize, targetTransitionDuration);

        yield return new WaitForSeconds(impactHoldDuration);
        yield return MoveView(OverviewPosition, OverviewSize, returnDuration);
        atLauncher = false;
        IsBusy = false;
    }

    public void FocusLauncher()
    {
        if (!CanNavigate)
            return;
        StopViewTransition();
        viewTransition = StartCoroutine(FocusLauncherRoutine());
    }

    private IEnumerator FocusLauncherRoutine()
    {
        atLauncher = true;
        yield return MoveView(LauncherPosition, launcherSize, launchZoomDuration);
        viewTransition = null;
        onLauncherFocused.Invoke();
    }

    public void ShowOverview()
    {
        if (!CanNavigate)
            return;
        StopViewTransition();
        viewTransition = StartCoroutine(OverviewRoutine());
    }

    private IEnumerator OverviewRoutine()
    {
        yield return MoveView(OverviewPosition, OverviewSize, returnDuration);
        atLauncher = false;
        viewTransition = null;
    }

    private void StopViewTransition()
    {
        if (viewTransition == null)
            return;
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

    // Minimal controls until the game's own HUD / upgrade menu is ready.
    private const float ControlsWidth = 290f;
    private const float ControlsHeight = 108f;
    private float ControlsScale => Mathf.Clamp(Screen.height / 720f, 0.6f, 2f);
    public bool IsPointerOverControls(Vector2 screenPosition)
    {
        if (!showControls || !CanNavigate)
            return false;
        Vector2 guiPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y)
            / ControlsScale;
        return new Rect(12f, 12f, ControlsWidth, ControlsHeight).Contains(guiPosition);
    }

    private void OnGUI()
    {
        if (!showControls || slingshot == null || !slingshot.isActiveAndEnabled
            || IsBusy || Time.timeScale <= 0f)
            return;
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(Vector3.one * ControlsScale);
        GUI.Box(new Rect(12f, 12f, ControlsWidth, ControlsHeight), "Катапульта");
        GUI.enabled = !slingshot.IsDragging;
        if (GUI.Button(new Rect(24f, 40f, 130f, 30f), "К катапульте"))
            FocusLauncher();
        if (GUI.Button(new Rect(164f, 40f, 126f, 30f), "Вся карта"))
            ShowOverview();
        GUI.enabled = true;
        GUI.Label(new Rect(24f, 78f, 268f, 30f), "Выстрел: потяните снаряд и отпустите");
        GUI.matrix = previous;
    }

    private void OnDisable()
    {
        if (slingshot != null)
        {
            slingshot.ShotLaunched -= OnShotLaunched;
            slingshot.ProjectileImpacted -= OnProjectileImpacted;
        }
        StopAllCoroutines();
        viewTransition = null;
        IsBusy = false;
    }
}
