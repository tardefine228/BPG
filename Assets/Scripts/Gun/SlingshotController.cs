using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SlingshotController : MonoBehaviour
{
    [Header("References")]
    public Rigidbody2D projectilePrefab;
    public Transform launchPoint;
    public Transform rotatingPart;
    public LineRenderer aimLine;
    public Transform pivotPoint;
    public GameplayCameraController gameplayCamera;

    [Header("Shot physics")]
    public float maxPullDistance = 2.5f;
    public float launchPower = 22f;
    public float projectileGravityScale = 2f;
    [Min(0f)] public float airResistance = 0.06f;
    public LayerMask impactLayers = ~0;

    [Header("Dotted trajectory")]
    public int trajectoryPoints = 180;
    public float timeStep = 0.04f;
    public float trajectoryDotPixels = 7f;
    public float dotTimeSpacing = 0.12f;
    public Color weakPullColor = new Color(1f, 0.2f, 0.16f);
    public Color mediumPullColor = new Color(1f, 0.77f, 0.12f);
    public Color strongPullColor = new Color(0.23f, 0.8f, 0.35f);

    [Header("Aiming from the overview")]
    public float grabRadiusPixels = 24f;
    [Range(0.05f, 0.5f)] public float fullPullScreenFraction = 0.2f;

    [Header("Reload")]
    public float spawnDelay = 2.5f;
    public float projectileLifetime = 12f;

    public event Action<Rigidbody2D> ShotLaunched;
    public event Action<Vector2> ProjectileImpacted;
    public int ShotsFired { get; private set; }
    public bool IsDragging => isDragging;
    public bool CanAim => projectile != null && !shotInProgress;
    public float ProjectileRadius { get; private set; } = 0.5f;

    private readonly RaycastHit2D[] predictionHits = new RaycastHit2D[16];
    private readonly List<SpriteRenderer> dots = new List<SpriteRenderer>();
    private Rigidbody2D projectile;
    private Camera mainCamera;
    private bool isDragging;
    private bool shotInProgress;
    private Vector2 launchVelocity;
    private Vector2 dragStartScreen;
    private Transform dotsRoot;
    private Color trajectoryColor;
    private int visibleDots;

    private void Start()
    {
        mainCamera = Camera.main;
        ShotsFired = 0;
        CircleCollider2D circle = projectilePrefab.GetComponent<CircleCollider2D>();
        if (circle != null)
            ProjectileRadius = circle.radius * Mathf.Max(
                Mathf.Abs(projectilePrefab.transform.lossyScale.x),
                Mathf.Abs(projectilePrefab.transform.lossyScale.y));
        dotsRoot = new GameObject("TrajectoryDots").transform;
        dotsRoot.SetParent(transform, false);
        if (aimLine != null)
            aimLine.enabled = false;
        SpawnProjectile();
    }

    private void SpawnProjectile()
    {
        projectile = Instantiate(projectilePrefab, launchPoint.position, Quaternion.identity);
        projectile.bodyType = RigidbodyType2D.Kinematic;
        projectile.simulated = false;
        projectile.gravityScale = projectileGravityScale;
        projectile.linearDamping = Mathf.Max(0f, airResistance);
        projectile.linearVelocity = Vector2.zero;
        projectile.interpolation = RigidbodyInterpolation2D.Interpolate;
        projectile.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        isDragging = false;
        shotInProgress = false;
        launchVelocity = Vector2.zero;
        ClearTrajectory();
    }

    private void Update()
    {
        if (!CanAim || mainCamera == null || Mouse.current == null || SceneTransition.IsLoading)
            return;
        if (gameplayCamera != null && gameplayCamera.IsBusy)
            return;

        Vector2 pointer = Mouse.current.position.ReadValue();
        if (Mouse.current.leftButton.wasPressedThisFrame
            && (gameplayCamera == null || !gameplayCamera.IsPointerOverControls(pointer)))
        {
            Vector2 shellScreen = mainCamera.WorldToScreenPoint(projectile.position);
            float visibleRadius = ProjectileRadius * mainCamera.pixelHeight
                / (2f * mainCamera.orthographicSize);
            if (Vector2.Distance(pointer, shellScreen) <= Mathf.Max(grabRadiusPixels, visibleRadius))
            {
                isDragging = true;
                dragStartScreen = pointer;
            }
        }

        if (isDragging && Mouse.current.leftButton.isPressed)
            Aim(pointer);
        if (isDragging && Mouse.current.leftButton.wasReleasedThisFrame)
        {
            isDragging = false;
            if (launchVelocity.sqrMagnitude < 1f)
                ClearTrajectory();
            else
                StartCoroutine(LaunchRoutine());
        }
    }

    private void Aim(Vector2 pointer)
    {
        float fullPullPixels = Mathf.Max(1f, mainCamera.pixelHeight * fullPullScreenFraction);
        Vector2 pull = Vector2.ClampMagnitude(
            (dragStartScreen - pointer) * maxPullDistance / fullPullPixels, maxPullDistance);
        if (pull.sqrMagnitude < 0.001f)
        {
            launchVelocity = Vector2.zero;
            ClearTrajectory();
            return;
        }

        float angle = Mathf.Clamp(Mathf.Atan2(pull.y, pull.x) * Mathf.Rad2Deg, -20f, 70f);
        rotatingPart.rotation = Quaternion.Euler(0f, 0f, angle);
        float radians = angle * Mathf.Deg2Rad;
        launchVelocity = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians))
            * pull.magnitude * launchPower;
        projectile.position = launchPoint.position;
        projectile.rotation = angle;
        float strength = pull.magnitude / Mathf.Max(0.01f, maxPullDistance);
        trajectoryColor = strength < 0.5f
            ? Color.Lerp(weakPullColor, mediumPullColor, strength * 2f)
            : Color.Lerp(mediumPullColor, strongPullColor, (strength - 0.5f) * 2f);
        PredictTrajectory(projectile.position, launchVelocity, projectile.gravityScale,
            true, out _, out _, out _);
    }

    public bool TryPredictImpact(Vector2 position, Vector2 velocity, float gravityScale,
        out Vector2 impactPoint, out float flightTime, out Collider2D hitCollider)
    {
        return PredictTrajectory(position, velocity, gravityScale, false,
            out impactPoint, out flightTime, out hitCollider);
    }

    private bool PredictTrajectory(Vector2 start, Vector2 velocity, float gravityScale,
        bool draw, out Vector2 impactPoint, out float flightTime, out Collider2D hitCollider)
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(impactLayers);
        filter.useTriggers = false;
        Vector2 gravity = Physics2D.gravity * gravityScale;
        Vector2 previous = start;
        float step = Mathf.Max(0.001f, Time.fixedDeltaTime);
        float horizon = Mathf.Max(2, trajectoryPoints) * Mathf.Max(0.01f, timeStep);
        int steps = Mathf.CeilToInt(horizon / step);
        float damping = 1f / (1f + Mathf.Max(0f, airResistance) * step);
        float dotSpacing = Mathf.Max(step, dotTimeSpacing);
        float nextDotTime = dotSpacing;
        if (draw)
            visibleDots = 0;
        impactPoint = start;
        flightTime = 0f;
        hitCollider = null;

        for (int i = 1; i <= steps; i++)
        {
            // Match the fixed-step Physics2D integrator, including air resistance.
            velocity = (velocity + gravity * step) * damping;
            Vector2 point = previous + velocity * step;
            Vector2 segment = point - previous;
            float distance = segment.magnitude;
            if (distance > 0f)
            {
                int count = Physics2D.CircleCast(previous, ProjectileRadius,
                    segment / distance, filter, predictionHits, distance);
                float nearest = float.PositiveInfinity;
                for (int h = 0; h < count; h++)
                {
                    RaycastHit2D hit = predictionHits[h];
                    if (hit.rigidbody == projectile || hit.distance >= nearest)
                        continue;
                    nearest = hit.distance;
                    hitCollider = hit.collider;
                    impactPoint = hit.point;
                    flightTime = (i - 1) * step + step * hit.fraction;
                    point = hit.centroid;
                }
                if (hitCollider != null)
                {
                    if (draw)
                    {
                        DrawDot(point);
                        HideUnusedDots();
                    }
                    return true;
                }
            }
            float t = i * step;
            if (draw && t + 0.0001f >= nextDotTime)
            {
                DrawDot(point);
                nextDotTime += dotSpacing;
            }
            previous = point;
            impactPoint = point;
            flightTime = t;
        }
        if (draw)
            HideUnusedDots();
        return false;
    }

    private void DrawDot(Vector2 position)
    {
        if (visibleDots == dots.Count)
        {
            SpriteRenderer original = projectilePrefab.GetComponent<SpriteRenderer>();
            SpriteRenderer dot = new GameObject("Dot_" + dots.Count).AddComponent<SpriteRenderer>();
            dot.transform.SetParent(dotsRoot, false);
            dot.sprite = original.sprite;
            dot.sharedMaterial = original.sharedMaterial;
            dot.sortingOrder = 100;
            dots.Add(dot);
        }
        SpriteRenderer renderer = dots[visibleDots++];
        float diameter = 2f * mainCamera.orthographicSize * trajectoryDotPixels
            / Mathf.Max(1, mainCamera.pixelHeight);
        renderer.transform.position = new Vector3(position.x, position.y, launchPoint.position.z);
        renderer.transform.localScale = Vector3.one * diameter / renderer.sprite.bounds.size.x;
        Color color = trajectoryColor;
        color.a *= Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(visibleDots * dotTimeSpacing
            / (trajectoryPoints * Mathf.Max(0.01f, timeStep))));
        renderer.color = color;
        renderer.gameObject.SetActive(true);
    }

    private void HideUnusedDots()
    {
        for (int i = visibleDots; i < dots.Count; i++)
            dots[i].gameObject.SetActive(false);
    }

    private IEnumerator LaunchRoutine()
    {
        shotInProgress = true;
        ClearTrajectory();
        if (gameplayCamera != null && gameplayCamera.isActiveAndEnabled)
            yield return gameplayCamera.PrepareForShot();

        Rigidbody2D fired = projectile;
        ProjectileImpactReporter reporter = fired.GetComponent<ProjectileImpactReporter>();
        if (reporter == null)
            reporter = fired.gameObject.AddComponent<ProjectileImpactReporter>();
        bool impacted = false;
        reporter.Arm(point =>
        {
            impacted = true;
            gameplayCamera?.RecordImpact(point, reporter.HitCollider);
            ProjectileImpacted?.Invoke(point);
        });
        fired.bodyType = RigidbodyType2D.Dynamic;
        fired.simulated = true;
        fired.linearVelocity = launchVelocity;
        ShotsFired++;
        AudioManager.Instance?.PlayShotSound();
        gameplayCamera?.FollowShot(fired);
        ShotLaunched?.Invoke(fired);
        Destroy(fired.gameObject, projectileLifetime);

        float flightDeadline = Time.time + Mathf.Min(projectileLifetime, 7f);
        while (fired != null && !impacted && Time.time < flightDeadline)
            yield return null;
        yield return new WaitForSeconds(spawnDelay);
        while (gameplayCamera != null && gameplayCamera.IsBusy)
            yield return null;
        SpawnProjectile();
    }

    private void ClearTrajectory()
    {
        if (aimLine != null)
            aimLine.positionCount = 0;
        visibleDots = 0;
        HideUnusedDots();
    }

    public void CancelInput()
    {
        isDragging = false;
        // Pausing the pre-shot hold must preserve the committed velocity.
        if (!shotInProgress)
            launchVelocity = Vector2.zero;
        ClearTrajectory();
    }

    public void Reload()
    {
        if (shotInProgress)
            return;
        if (projectile != null)
            Destroy(projectile.gameObject);
        SpawnProjectile();
    }

    private void OnDestroy()
    {
        if (dotsRoot != null)
            Destroy(dotsRoot.gameObject);
        if (projectile != null)
            Destroy(projectile.gameObject);
    }
}
