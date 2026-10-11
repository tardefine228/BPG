using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class SlingshotController : MonoBehaviour
{
    [Header("Ссылки")]
    public Rigidbody2D projectilePrefab;
    public Transform launchPoint;
    public Transform rotatingPart;
    public LineRenderer aimLine;
    public Transform pivotPoint;
    public GameplayCameraController gameplayCamera;

    [Header("Коты в очереди")]
    [Tooltip("Пять видов котов. Порядок списка задаёт порядок выстрелов.")]
    [SerializeField] private Rigidbody2D[] catProjectilePrefabs;
    [Tooltip("Место первого ожидающего кота позади катапульты.")]
    [SerializeField] private Transform queueOrigin;
    [SerializeField] private Vector2 queueSpacing = new Vector2(-1.4f, 0f);
    [SerializeField, Min(0f)] private float catLoadDuration = 0.35f;

    [Header("Физика выстрела")]
    public float maxPullDistance = 2.5f;
    public float launchPower = 22f;
    public float projectileGravityScale = 2f;
    [Min(0f)] public float airResistance = 0.06f;
    public LayerMask impactLayers = ~0;

    [Header("Точечная траектория")]
    public int trajectoryPoints = 180;
    public float timeStep = 0.04f;
    public float trajectoryDotPixels = 7f;
    public float dotTimeSpacing = 0.12f;
    public Color weakPullColor = new Color(1f, 0.2f, 0.16f);
    public Color mediumPullColor = new Color(1f, 0.77f, 0.12f);
    public Color strongPullColor = new Color(0.23f, 0.8f, 0.35f);

    [Header("Прицеливание из общего вида")]
    public float grabRadiusPixels = 24f;
    [Range(0.05f, 0.5f)] public float fullPullScreenFraction = 0.2f;

    [Header("Звуки кота (громкость эффектов)")]
    [SerializeField] private AudioClip grabMeow;
    [SerializeField] private AudioClip flightMeow;
    [SerializeField, Range(0f, 1f)] private float grabMeowVolume = 0.7f;
    [SerializeField, Range(0f, 1f)] private float flightMeowVolume = 0.9f;

    [Header("Перезарядка")]
    public float spawnDelay = 2.5f;
    public float projectileLifetime = 12f;

    public event Action<Rigidbody2D> ShotLaunched;
    public event Action<Vector2> ProjectileImpacted;
    public int ShotsFired { get; private set; }
    public int WaitingCatCount => waitingCats.Count;
    public bool IsDragging => isDragging;
    public bool IsShotInProgress => shotInProgress;
    public bool CanAim => isActiveAndEnabled && projectile != null && !shotInProgress && CanLoadProjectile;
    public float ProjectileRadius { get; private set; } = 0.5f;

    private LevelSession levelSession;
    private readonly List<Rigidbody2D> launchedProjectiles = new List<Rigidbody2D>();
    private readonly List<Rigidbody2D> waitingCats = new List<Rigidbody2D>();
    private bool CanLoadProjectile => levelSession == null
        || (!levelSession.IsEnding && levelSession.RemainingShots > 0);

    // Ждёт завершения полёта и ухода котов за поле перед показом поражения.
    public bool HasMovingProjectiles
    {
        get
        {
            for (int i = launchedProjectiles.Count - 1; i >= 0; i--)
            {
                Rigidbody2D fired = launchedProjectiles[i];
                if (fired == null)
                {
                    launchedProjectiles.RemoveAt(i);
                    continue;
                }
                if (fired.GetComponent<CatFlightEffects>() is CatFlightEffects effects && effects.IsExiting)
                    return true;
                if (fired.simulated && !fired.IsSleeping()
                    && (fired.linearVelocity.sqrMagnitude > 0.0225f
                        || Mathf.Abs(fired.angularVelocity) > 12f))
                    return true;
            }
            return false;
        }
    }

    private readonly RaycastHit2D[] predictionHits = new RaycastHit2D[16];
    [SerializeField] private List<SpriteRenderer> dots = new List<SpriteRenderer>();
    private Rigidbody2D projectile;
    private CatAbility activeAbility;
    private Camera mainCamera;
    private bool isDragging;
    private bool shotInProgress;
    private Vector2 launchVelocity;
    private Vector2 dragStartScreen;
    private Color trajectoryColor;
    private int visibleDots;

    private void Awake()
    {
        levelSession = GetComponent<LevelSession>();
        if (grabMeow == null)
            grabMeow = Resources.Load<AudioClip>("Audio/Slingshot/CatMeowShort");
        if (flightMeow == null)
            flightMeow = Resources.Load<AudioClip>("Audio/Slingshot/CatMeowLong");
    }

    private void Start()
    {
        mainCamera = Camera.main;
        ShotsFired = 0;
        if (aimLine != null)
            aimLine.enabled = false;
        CreateCats();
        LoadFirstCat();
    }

    private void CreateCats()
    {
        int count = levelSession != null ? levelSession.MaxProjectiles :
            (catProjectilePrefabs != null && catProjectilePrefabs.Length > 0 ? catProjectilePrefabs.Length : 5);
        for (int i = 0; i < count; i++)
        {
            Rigidbody2D prefab = catProjectilePrefabs != null && catProjectilePrefabs.Length > 0
                ? catProjectilePrefabs[i % catProjectilePrefabs.Length] : projectilePrefab;
            Rigidbody2D cat = Instantiate(prefab, QueuePosition(Mathf.Max(0, i - 1)),
                Quaternion.identity, queueOrigin);
            cat.bodyType = RigidbodyType2D.Kinematic;
            cat.simulated = false;
            cat.gravityScale = projectileGravityScale;
            cat.linearDamping = Mathf.Max(0f, airResistance);
            cat.linearVelocity = Vector2.zero;
            cat.interpolation = RigidbodyInterpolation2D.Interpolate;
            cat.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            cat.GetComponent<CatProjectile>()?.ShowQueued();
            waitingCats.Add(cat);
        }
    }

    private Vector3 QueuePosition(int index)
    {
        Vector3 origin = queueOrigin != null ? queueOrigin.position :
            launchPoint.position + new Vector3(-3f, -1.5f, 0f);
        return origin + (Vector3)(queueSpacing * index);
    }

    private void LoadFirstCat()
    {
        if (!CanLoadProjectile || waitingCats.Count == 0)
            return;
        projectile = waitingCats[0];
        waitingCats.RemoveAt(0);
        SetLoadedPose();
        projectile.transform.SetPositionAndRotation(launchPoint.position, rotatingPart.rotation);
        for (int i = 0; i < waitingCats.Count; i++)
            waitingCats[i].transform.position = QueuePosition(i);
        isDragging = false;
        shotInProgress = false;
        launchVelocity = Vector2.zero;
        ClearTrajectory();
    }

    private void SetLoadedPose()
    {
        projectile.GetComponent<CatProjectile>()?.ShowLoaded();
        CircleCollider2D circle = projectile.GetComponent<CircleCollider2D>();
        if (circle != null)
            ProjectileRadius = circle.radius * Mathf.Max(
                Mathf.Abs(projectile.transform.lossyScale.x), Mathf.Abs(projectile.transform.lossyScale.y));
    }

    private IEnumerator LoadNextCat()
    {
        if (!CanLoadProjectile || waitingCats.Count == 0)
            yield break;
        projectile = waitingCats[0];
        waitingCats.RemoveAt(0);
        SetLoadedPose();
        Vector3 from = projectile.transform.position;
        Vector3[] queueFrom = new Vector3[waitingCats.Count];
        for (int i = 0; i < queueFrom.Length; i++)
            queueFrom[i] = waitingCats[i].transform.position;
        float elapsed = 0f;
        while (elapsed < catLoadDuration)
        {
            // Time.deltaTime ещё может относиться к кадру до нажатия паузы.
            if (Time.timeScale <= 0f)
            {
                yield return null;
                continue;
            }
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / catLoadDuration));
            projectile.transform.SetPositionAndRotation(
                Vector3.Lerp(from, launchPoint.position, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.6f),
                rotatingPart.rotation);
            for (int i = 0; i < queueFrom.Length; i++)
                waitingCats[i].transform.position = Vector3.Lerp(queueFrom[i], QueuePosition(i), t);
            yield return null;
        }
        projectile.transform.SetPositionAndRotation(launchPoint.position, rotatingPart.rotation);
        for (int i = 0; i < waitingCats.Count; i++)
            waitingCats[i].transform.position = QueuePosition(i);
    }

    private void Update()
    {
        HandleAbilityInput();
        if (!CanAim || Time.timeScale <= 0f || mainCamera == null || Mouse.current == null || SceneTransition.IsLoading)
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
                AudioManager.Instance?.PlaySfx(grabMeow, grabMeowVolume);
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

    // Повторное нажатие ЛКМ (не по UI) или пробела использует способность
    // уже выпущенного кота. Первый клик для натяжения не активирует её.
    private void HandleAbilityInput()
    {
        if (activeAbility == null || !activeAbility.CanActivate || Time.timeScale <= 0f
            || SceneTransition.IsLoading)
            return;

        bool space = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        bool click = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        if (!space && !click)
            return;

        if (!space && click)
        {
            Vector2 pointer = Mouse.current.position.ReadValue();
            if ((gameplayCamera != null && gameplayCamera.IsPointerOverControls(pointer))
                || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
                return;
        }

        if (activeAbility.TryActivate())
            activeAbility = null;
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
            // Прогноз повторяет фиксированный шаг физики, включая сопротивление воздуха.
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
        if (visibleDots >= dots.Count)
            return;
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
        if (!CanAim)
            yield break;
        shotInProgress = true;
        ClearTrajectory();
        if (gameplayCamera != null && gameplayCamera.isActiveAndEnabled)
            yield return gameplayCamera.PrepareForShot();

        Rigidbody2D fired = projectile;
        fired.transform.SetParent(null, true);
        ProjectileImpactReporter reporter = fired.GetComponent<ProjectileImpactReporter>();
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
        activeAbility = fired.GetComponent<CatAbility>();
        if (activeAbility != null)
            activeAbility.Arm();
        ShotsFired++;
        launchedProjectiles.Add(fired);
        fired.GetComponent<ProjectileComicEffects>()?.BeginFlight();
        fired.GetComponent<CatFlightEffects>()?.BeginFlight(gameplayCamera != null ? gameplayCamera.mapMin.y : -15f);
        projectile = null;
        AudioManager.Instance?.PlayShotSound();
        AudioManager.Instance?.PlaySfx(flightMeow, flightMeowVolume);
        gameplayCamera?.FollowShot(fired);
        ShotLaunched?.Invoke(fired);
        Destroy(fired.gameObject, projectileLifetime);

        // Очередь убывает сразу после выстрела; следующий кот занимает катапульту,
        // пока камера показывает полёт предыдущего. Прицел доступен после её возврата.
        yield return LoadNextCat();

        float flightDeadline = Time.time + Mathf.Min(projectileLifetime, 7f);
        while (fired != null && !impacted && Time.time < flightDeadline)
            yield return null;
        yield return new WaitForSeconds(spawnDelay);
        while (gameplayCamera != null && gameplayCamera.IsBusy)
            yield return null;
        shotInProgress = false;
        launchVelocity = Vector2.zero;
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
        // Пауза перед выстрелом сохраняет уже выбранную скорость запуска.
        if (!shotInProgress)
            launchVelocity = Vector2.zero;
        ClearTrajectory();
    }

    public void Reload()
    {
        if (shotInProgress || !CanLoadProjectile || !isActiveAndEnabled)
            return;
        // Возврат камеры к катапульте не меняет кота и не расходует боезапас.
        if (projectile != null)
        {
            CancelInput();
            projectile.transform.SetPositionAndRotation(launchPoint.position, rotatingPart.rotation);
        }
    }

    private void OnDestroy()
    {
        if (projectile != null)
            Destroy(projectile.gameObject);
        for (int i = 0; i < waitingCats.Count; i++)
            if (waitingCats[i] != null)
                Destroy(waitingCats[i].gameObject);
        for (int i = 0; i < launchedProjectiles.Count; i++)
            if (launchedProjectiles[i] != null)
                Destroy(launchedProjectiles[i].gameObject);
    }
}
