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

    [Header("Settings")]
    public float maxPullDistance = 2.5f;
    public float launchPower = 20f;
    public float projectileGravityScale = 2f;
    public int trajectoryPoints = 180;
    public float timeStep = 0.04f;
    public float trajectoryWidthPixels = 3f;
    public LayerMask impactLayers = ~0;

    [Header("Aiming from the overview")]
    public float grabRadiusPixels = 24f;
    [Range(0.05f, 0.5f)] public float fullPullScreenFraction = 0.2f;

    [Header("Reload Settings")]
    public float spawnDelay = 2.5f;
    public float projectileLifetime = 12f;

    [Header("След траектории (точки)")]
    [Tooltip("Рисовать путь снаряда точками вместо тонкой линии.")]
    [SerializeField] private bool useDottedTrajectory = true;

    [Tooltip("Какую часть пути показывать: 0.5 — половину пути до мишени.")]
    [SerializeField, Range(0.1f, 1f)] private float trajectoryVisibleFraction = 0.5f;

    [Tooltip("Диаметр точки следа в пикселях экрана.")]
    [SerializeField, Min(2f)] private float trajectoryDotPixels = 16f;

    [Tooltip("Расстояние между точками следа в пикселях экрана.")]
    [SerializeField, Min(4f)] private float trajectoryDotGapPixels = 46f;

    [Tooltip("Цвет точки следа; к концу пути точки гаснут.")]
    [SerializeField] private Color trajectoryDotColor = Color.white;

    [Tooltip("Цвет обводки точки — чтобы след было видно на светлом фоне.")]
    [SerializeField] private Color trajectoryDotOutline = new Color32(48, 48, 48, 255);

    [Tooltip("Размер точки в конце следа относительно начала (1 — не уменьшать).")]
    [SerializeField, Range(0.3f, 1f)] private float trajectoryEndScale = 0.75f;

    [Header("Звук рогатки")]
    [Tooltip("Звук натяжения, играет пока игрок целится (можно задать свой файл).")]
    [SerializeField] private AudioClip aimSound;

    [Tooltip("Звук выстрела (можно задать свой файл).")]
    [SerializeField] private AudioClip shotSound;

    [SerializeField, Range(0f, 1f)] private float aimVolume = 0.55f;
    [SerializeField, Range(0f, 1f)] private float shotVolume = 0.9f;

    public event Action<Rigidbody2D> ShotLaunched;
    public event Action<Vector2> ProjectileImpacted;
    public int ShotsFired { get; private set; }
    public bool IsDragging => isDragging;
    public bool CanAim => projectile != null && !shotInProgress;
    public float ProjectileRadius { get; private set; } = 0.5f;

    private const int MaxTrailDots = 48;
    private const int TrailSortingOrder = 15;
    private const string AimSoundResourcePath = "Audio/Slingshot/SlingshotStretch";
    private const string ShotSoundResourcePath = "Audio/Slingshot/SlingshotShot";

    private readonly RaycastHit2D[] predictionHits = new RaycastHit2D[16];
    private readonly List<Vector2> previewPoints = new List<Vector2>();
    private TrajectoryDotTrail trajectoryTrail;
    private AudioSource stretchSource;
    private AudioSource oneShotSource;
    private Rigidbody2D projectile;
    private Camera mainCamera;
    private bool isDragging;
    private bool shotInProgress;
    private Vector2 launchVelocity;
    private Vector2 dragStartScreen;

    private void Awake()
    {
        if (aimSound == null)
        {
            aimSound = Resources.Load<AudioClip>(AimSoundResourcePath);
        }

        if (shotSound == null)
        {
            shotSound = Resources.Load<AudioClip>(ShotSoundResourcePath);
        }

        stretchSource = gameObject.AddComponent<AudioSource>();
        stretchSource.playOnAwake = false;
        stretchSource.loop = true;
        stretchSource.spatialBlend = 0f;
        stretchSource.dopplerLevel = 0f;
        stretchSource.volume = 0f;

        oneShotSource = gameObject.AddComponent<AudioSource>();
        oneShotSource.playOnAwake = false;
        oneShotSource.loop = false;
        oneShotSource.spatialBlend = 0f;
        oneShotSource.dopplerLevel = 0f;

        if (useDottedTrajectory)
        {
            trajectoryTrail = new TrajectoryDotTrail(transform, MaxTrailDots,
                TrailSortingOrder, trajectoryDotColor, trajectoryDotOutline);
            if (aimLine != null)
            {
                aimLine.enabled = false;
            }
        }
    }

    private void Start()
    {
        mainCamera = Camera.main;
        ShotsFired = 0;
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
        if (!isDragging)
        {
            StopStretchSound();
        }

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
                StartStretchSound();
            }
        }

        if (isDragging && Mouse.current.leftButton.isPressed)
            Aim(pointer);
        if (isDragging && Mouse.current.leftButton.wasReleasedThisFrame)
        {
            isDragging = false;
            StopStretchSound();
            if (launchVelocity.sqrMagnitude < 1f)
            {
                ClearTrajectory();
            }
            else
            {
                PlayShotSound();
                StartCoroutine(LaunchRoutine());
            }
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
            UpdateStretchSound(0f);
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
        UpdateStretchSound(pull.magnitude / Mathf.Max(0.01f, maxPullDistance));
        ShowTrajectoryPreview();
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
        if (draw)
        {
            previewPoints.Clear();
            if (aimLine != null && !useDottedTrajectory)
                aimLine.positionCount = 0;
        }
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
                    AddPreviewPoint(point, draw);
                    return true;
                }
            }
            AddPreviewPoint(point, draw);
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
        bool impacted = false;
        reporter.Arm(point =>
        {
            impacted = true;
            ProjectileImpacted?.Invoke(point);
        });
        fired.bodyType = RigidbodyType2D.Dynamic;
        fired.simulated = true;
        fired.linearVelocity = launchVelocity;
        ShotLaunched?.Invoke(fired);
        ShotsFired++;
        Destroy(fired.gameObject, projectileLifetime);

        float flightDeadline = Time.time + Mathf.Min(projectileLifetime, 7f);
        while (fired != null && !impacted && Time.time < flightDeadline)
            yield return null;
        yield return new WaitForSeconds(spawnDelay);
        SpawnProjectile();
    }

    private void ClearTrajectory()
    {
        if (aimLine != null)
            aimLine.positionCount = 0;
        previewPoints.Clear();
        if (trajectoryTrail != null)
        {
            trajectoryTrail.Hide();
        }
    }

    public void CancelInput()
    {
        StopStretchSound();
        isDragging = false;
        // Pausing during the return to overview must preserve the committed shot.
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
        if (projectile != null)
            Destroy(projectile.gameObject);

        if (trajectoryTrail != null)
        {
            trajectoryTrail.Dispose();
            trajectoryTrail = null;
        }
    }

    // -------------------------------------------------------- trail & sound ----
    /// <summary>Stores the predicted point and updates the fallback line renderer.</summary>
    private void AddPreviewPoint(Vector2 point, bool draw)
    {
        if (!draw)
        {
            return;
        }

        previewPoints.Add(point);
        if (aimLine != null && !useDottedTrajectory)
        {
            aimLine.positionCount = previewPoints.Count;
            aimLine.SetPosition(previewPoints.Count - 1, point);
        }
    }

    /// <summary>
    /// Shows the first part of the predicted path as dots, sized in screen
    /// pixels so the trail looks the same at both camera distances.
    /// </summary>
    private void ShowTrajectoryPreview()
    {
        if (trajectoryTrail == null || mainCamera == null || previewPoints.Count < 2)
        {
            return;
        }

        float unitsPerPixel = 2f * mainCamera.orthographicSize
            / Mathf.Max(1, mainCamera.pixelHeight);
        trajectoryTrail.Show(previewPoints, trajectoryVisibleFraction,
            trajectoryDotPixels * unitsPerPixel,
            trajectoryDotGapPixels * unitsPerPixel,
            trajectoryEndScale);
    }

    private float EffectsVolumePercent => AudioManager.Instance != null
        ? AudioManager.Instance.EffectsVolume
        : 1f;

    private void StartStretchSound()
    {
        if (stretchSource == null || aimSound == null)
        {
            return;
        }

        stretchSource.clip = aimSound;
        stretchSource.pitch = 0.9f;
        stretchSource.volume = 0f;
        stretchSource.Play();
    }

    /// <summary>Pitch and volume grow with the pull, so the tension is audible.</summary>
    private void UpdateStretchSound(float pullFraction)
    {
        if (stretchSource == null || !stretchSource.isPlaying)
        {
            return;
        }

        float tension = Mathf.Clamp01(pullFraction);
        stretchSource.volume = aimVolume * Mathf.Lerp(0.3f, 1f, tension) * EffectsVolumePercent;
        stretchSource.pitch = Mathf.Lerp(0.85f, 1.25f, tension);
    }

    private void StopStretchSound()
    {
        if (stretchSource != null && stretchSource.isPlaying)
        {
            stretchSource.Stop();
        }
    }

    private void PlayShotSound()
    {
        if (shotSound == null)
        {
            return;
        }

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySfx(shotSound, shotVolume);
            return;
        }

        if (oneShotSource != null)
        {
            oneShotSource.volume = shotVolume * EffectsVolumePercent;
            oneShotSource.PlayOneShot(shotSound);
        }
    }
}
