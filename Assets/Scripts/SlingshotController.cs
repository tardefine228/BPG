using System;
using System.Collections;
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

    [Header("Settings")]
    public float maxPullDistance = 2.5f;
    public float launchPower = 40f;
    public float projectileGravityScale = 4f;
    public int trajectoryPoints = 180;
    public float timeStep = 0.04f;
    public float trajectoryWidthPixels = 3f;
    public LayerMask impactLayers = ~0;

    [Header("Aiming from the overview")]
    public float grabRadiusPixels = 24f;
    [Range(0.05f, 0.5f)] public float fullPullScreenFraction = 0.2f;

    [Header("Reload Settings")]
    public float spawnDelay = 1.5f;
    public float projectileLifetime = 12f;

    public event Action<Rigidbody2D> ShotLaunched;
    public event Action<Vector2> ProjectileImpacted;
    public bool IsDragging => isDragging;
    public bool CanAim => projectile != null && !shotInProgress;
    public float ProjectileRadius { get; private set; } = 0.5f;

    private readonly RaycastHit2D[] predictionHits = new RaycastHit2D[16];
    private Rigidbody2D projectile;
    private Camera mainCamera;
    private bool isDragging;
    private bool shotInProgress;
    private Vector2 launchVelocity;
    private Vector2 dragStartScreen;

    private void Start()
    {
        mainCamera = Camera.main;
        CircleCollider2D circle = projectilePrefab.GetComponent<CircleCollider2D>();
        if (circle != null)
            ProjectileRadius = circle.radius * Mathf.Max(
                Mathf.Abs(projectilePrefab.transform.lossyScale.x),
                Mathf.Abs(projectilePrefab.transform.lossyScale.y));
        SpawnProjectile();
    }

    private void SpawnProjectile()
    {
        projectile = Instantiate(projectilePrefab, launchPoint.position, Quaternion.identity);
        projectile.bodyType = RigidbodyType2D.Kinematic;
        // A loaded shell must not collide with the world or its trajectory prediction.
        projectile.simulated = false;
        projectile.gravityScale = projectileGravityScale;
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
        if (!CanAim || mainCamera == null || Mouse.current == null)
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
        // Screen-space pulling gives the same precision at both camera distances.
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
        if (aimLine != null)
            aimLine.widthMultiplier = 2f * mainCamera.orthographicSize * trajectoryWidthPixels
                / Mathf.Max(1, mainCamera.pixelHeight);
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
        int points = Mathf.Max(2, trajectoryPoints);
        float step = Mathf.Max(0.01f, timeStep);
        if (draw && aimLine != null)
            aimLine.positionCount = points;
        impactPoint = start;
        flightTime = 0f;
        hitCollider = null;

        for (int i = 0; i < points; i++)
        {
            float t = i * step;
            // Account for the semi-implicit gravity step used by Physics2D.
            Vector2 point = start + velocity * t
                + 0.5f * gravity * t * (t + Time.fixedDeltaTime);
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
                    if (draw && aimLine != null)
                    {
                        aimLine.SetPosition(i, point);
                        aimLine.positionCount = i + 1;
                    }
                    return true;
                }
            }
            if (draw && aimLine != null)
                aimLine.SetPosition(i, point);
            previous = point;
            impactPoint = point;
            flightTime = t;
        }
        return false;
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
        reporter.Arm(point => ProjectileImpacted?.Invoke(point));
        fired.bodyType = RigidbodyType2D.Dynamic;
        fired.simulated = true;
        fired.linearVelocity = launchVelocity;
        ShotLaunched?.Invoke(fired);
        Destroy(fired.gameObject, projectileLifetime);

        yield return new WaitForSeconds(spawnDelay);
        while (gameplayCamera != null && gameplayCamera.IsBusy)
            yield return null;
        SpawnProjectile();
    }

    private void ClearTrajectory()
    {
        if (aimLine != null)
            aimLine.positionCount = 0;
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
        if (projectile != null)
            Destroy(projectile.gameObject);
    }
}
