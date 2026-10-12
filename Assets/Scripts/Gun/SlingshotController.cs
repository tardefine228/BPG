using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class SlingshotController : MonoBehaviour
{
    [Header("Ссылки")]
    public Rigidbody2D projectilePrefab;
    public Transform launchPoint;
    public Transform rotatingPart;
    public LineRenderer aimLine;
    public Transform pivotPoint;
    public GameplayCameraController gameplayCamera;

    [Header("Поворот катапульты")]
    [Range(0f, 80f)] public float minLaunchAngle = 0f;
    [Range(0f, 80f)] public float maxLaunchAngle = 65f;
    [Tooltip("Наклон назад, в сторону очереди, от сохранённой центральной позы.")]
    [Range(0f, 45f)] public float maxArmTilt = 35f;

    [Header("Коты в очереди")]
    [Tooltip("Пять видов котов. Порядок списка задаёт порядок выстрелов.")]
    [SerializeField] private Rigidbody2D[] catProjectilePrefabs;
    [Tooltip("Место первого ожидающего кота позади катапульты.")]
    [SerializeField] private Transform queueOrigin;
    [Tooltip("Отдельные места вокруг катапульты. Высота подбирается по коллайдеру земли; пустой список использует линейную очередь.")]
    [SerializeField] private Transform[] queueSlots;
    [SerializeField, Min(0f)] private float queueSurfaceClearance = 0.04f;
    [SerializeField] private Vector2 queueSpacing = new Vector2(-1.4f, 0f);
    [SerializeField, Min(0f)] private float catLoadDuration = 0.35f;

    [Header("Физика выстрела")]
    public float maxPullDistance = 2.5f;
    public float launchPower = 22f;
    [Tooltip("Уменьшайте для большей дальности без увеличения начальной скорости. Также поднимает дугу полёта.")]
    [Min(0.1f)] public float projectileGravityScale = 2f;
    [Min(0f)] public float airResistance = 0.06f;
    public LayerMask impactLayers = ~0;

    [Header("Точечная траектория")]
    public int trajectoryPoints = 180;
    public float timeStep = 0.04f;
    public float trajectoryDotPixels = 7f;
    [Tooltip("Размер точек при слабом и полном натяжении относительно Trajectory Dot Pixels.")]
    [Range(0.3f, 2f)] public float weakPullDotScale = 0.65f;
    [Range(0.3f, 2f)] public float strongPullDotScale = 1.25f;
    public float dotTimeSpacing = 0.12f;
    public Color weakPullColor = new Color(1f, 0.64f, 0.60f);
    public Color mediumPullColor = new Color(1f, 0.91f, 0.58f);
    public Color strongPullColor = new Color(0.65f, 0.95f, 0.72f);
    public Vector2 trajectoryShadowOffsetPixels = new Vector2(-2f, -2f);
    public Color trajectoryShadowColor = new Color(0.16f, 0.12f, 0.10f, 0.28f);

    [Header("Прицеливание из общего вида")]
    public float grabRadiusPixels = 24f;
    [Range(0.05f, 0.5f)] public float fullPullScreenFraction = 0.2f;

    [Header("Звуки натяжения катапульты и кота (громкость эффектов)")]
    [SerializeField] private AudioClip grabMeow;
    [Tooltip("Играет после мяуканья, пока катапульту держат натянутой; останавливается при отпускании и не повторяется.")]
    [SerializeField] private AudioClip pullSound;
    [SerializeField] private AudioClip flightMeow;
    [SerializeField, Range(0f, 1f)] private float grabMeowVolume = 0.7f;
    [SerializeField, Range(0f, 1f)] private float pullSoundVolume = 0.7f;
    [SerializeField, Range(0f, 1f)] private float flightMeowVolume = 0.9f;

    [Header("Перезарядка")]
    [Tooltip("Пауза после завершения полёта перед следующим выстрелом, в игровых секундах.")]
    [Min(0f)] public float spawnDelay = 2f;
    public float projectileLifetime = 12f;

    [Header("Подсказки выстрела")]
    [SerializeField] private bool shotHints = true;
    [SerializeField] private Vector2 abilityBadgeOffset = new Vector2(0f, 78f);
    [SerializeField] private Vector2 cancelHintOffset = new Vector2(-120f, -52f);

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
                if (fired.GetComponent<CatFlightEffects>() is CatFlightEffects effects && effects.HasPendingCleanup)
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
    private Quaternion restingArmRotation;
    private readonly List<SpriteRenderer> dotShadows = new List<SpriteRenderer>();
    private float trajectoryStrength;
    private const string AbilityHintsKey = "BPG.Guide.AbilityUsed.v2";
    private bool abilityHintShown;
    private bool IsFirstRound => gameObject.scene.name == LevelProgress.GameplaySceneName;
    private bool shotHadAbility;
    private bool showFlightInstruction;
    private bool abilityUsed;
    private Transform flightHintAnchor;
    private Vector3 flightHintPosition;
    private Canvas hintCanvas;
    private RectTransform hintCanvasRect;
    private RectTransform abilityBadge;
    private TMP_Text abilityBadgeLabel;
    private RectTransform cancelHint;
    private AudioSource pullSoundSource;
    private int pullSoundSequenceId;

    private void Awake()
    {
        levelSession = GetComponent<LevelSession>();
        restingArmRotation = rotatingPart.localRotation;
        if (grabMeow == null)
            grabMeow = Resources.Load<AudioClip>("Audio/Slingshot/CatMeowShort");
        if (flightMeow == null)
            flightMeow = Resources.Load<AudioClip>("Audio/Slingshot/CatMeowLong");

        pullSoundSource = gameObject.AddComponent<AudioSource>();
        pullSoundSource.volume = pullSoundVolume;
        pullSoundSource.playOnAwake = false;
        pullSoundSource.loop = false;
        pullSoundSource.spatialBlend = 0f;
        pullSoundSource.dopplerLevel = 0f;
    }

    private void Start()
    {
        mainCamera = Camera.main;
        ShotsFired = 0;
        if (aimLine != null)
            aimLine.enabled = false;
        // Геометрия Tilemap должна быть готова до расстановки котов.
        if (gameplayCamera != null && gameplayCamera.TerrainCollider is UnityEngine.Tilemaps.TilemapCollider2D tileCollider)
            tileCollider.ProcessTilemapChanges();
        Physics2D.SyncTransforms();
        foreach (SpriteRenderer dot in dots)
        {
            var shadow = new GameObject("TrajectoryDotShadow").AddComponent<SpriteRenderer>();
            shadow.transform.SetParent(dot.transform, false);
            shadow.sprite = dot.sprite;
            shadow.sharedMaterial = dot.sharedMaterial;
            shadow.sortingLayerID = dot.sortingLayerID;
            shadow.sortingOrder = dot.sortingOrder - 1;
            dotShadows.Add(shadow);
        }
        CreateCats();
        LoadFirstCat();
        abilityHintShown = PlayerPrefs.GetInt(AbilityHintsKey, 0) != 0;
        CreateShotHints();
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
            cat.interpolation = RigidbodyInterpolation2D.None;
            cat.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            cat.GetComponent<CatProjectile>()?.ShowQueued();
            cat.transform.position = QueuePosition(Mathf.Max(0, i - 1), cat);
            waitingCats.Add(cat);
        }
    }

    private Vector3 QueuePosition(int index, Rigidbody2D cat = null)
    {
        Vector3 origin = queueOrigin != null ? queueOrigin.position :
            launchPoint.position + new Vector3(-3f, -1.5f, 0f);
        Vector3 position = queueSlots != null && index < queueSlots.Length && queueSlots[index] != null
            ? queueSlots[index].position : origin + (Vector3)(queueSpacing * index);
        if (cat != null && gameplayCamera != null
            && gameplayCamera.TryGetSurfaceHeight(position.x, out float surfaceY))
        {
            SpriteRenderer sprite = cat.GetComponent<SpriteRenderer>();
            if (sprite != null && sprite.sprite != null)
                position.y = surfaceY + queueSurfaceClearance
                    - sprite.sprite.bounds.min.y * Mathf.Abs(cat.transform.lossyScale.y);
        }
        return position;
    }

    private void LoadFirstCat()
    {
        if (!CanLoadProjectile || waitingCats.Count == 0)
            return;
        projectile = waitingCats[0];
        waitingCats.RemoveAt(0);
        SetLoadedPose();
        ResetArm();
        SetLoadedTransform(launchPoint.position, Quaternion.identity);
        for (int i = 0; i < waitingCats.Count; i++)
            waitingCats[i].transform.position = QueuePosition(i, waitingCats[i]);
        isDragging = false;
        shotInProgress = false;
        launchVelocity = Vector2.zero;
        ClearTrajectory();
    }

    private void SetLoadedPose()
    {
        projectile.GetComponent<CatProjectile>()?.ShowLoaded();
        // Ковш — настоящий родитель кота до выстрела, поэтому спрайт следует
        // за ним и между физическими шагами, и при изменении позы катапульты.
        projectile.transform.SetParent(launchPoint, true);
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
            SetLoadedTransform(
                Vector3.Lerp(from, launchPoint.position, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.6f),
                Quaternion.identity);
            for (int i = 0; i < queueFrom.Length; i++)
                waitingCats[i].transform.position = Vector3.Lerp(queueFrom[i], QueuePosition(i, waitingCats[i]), t);
            yield return null;
        }
        SetLoadedTransform(launchPoint.position, Quaternion.identity);
        for (int i = 0; i < waitingCats.Count; i++)
            waitingCats[i].transform.position = QueuePosition(i, waitingCats[i]);
    }

    private void Update()
    {
        HandleAbilityInput();
        if (!CanAim || Time.timeScale <= 0f || mainCamera == null || Mouse.current == null || SceneTransition.IsLoading)
            return;
        if (gameplayCamera != null && gameplayCamera.IsBusy)
            return;

        Vector2 pointer = Mouse.current.position.ReadValue();
        if (isDragging && Mouse.current.rightButton.wasPressedThisFrame)
        {
            CancelInput();
            return;
        }
        if (Mouse.current.leftButton.wasPressedThisFrame
            && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
        {
            Vector2 shellScreen = mainCamera.WorldToScreenPoint(projectile.transform.position);
            float visibleRadius = ProjectileRadius * mainCamera.pixelHeight
                / (2f * mainCamera.orthographicSize);
            if (Vector2.Distance(pointer, shellScreen) <= Mathf.Max(grabRadiusPixels, visibleRadius))
            {
                StopPullSound();
                isDragging = true;
                AudioManager.Instance?.PlaySfx(grabMeow, grabMeowVolume);
                if (pullSound != null)
                    StartCoroutine(PlayPullSoundAfterMeow(pullSoundSequenceId));
                dragStartScreen = pointer;
            }
        }

        if (isDragging && Mouse.current.leftButton.isPressed)
            Aim(pointer);
        if (isDragging && Mouse.current.leftButton.wasReleasedThisFrame)
        {
            isDragging = false;
            StopPullSound();
            if (launchVelocity.sqrMagnitude < 1f)
                CancelInput();
            else
                StartCoroutine(LaunchRoutine());
        }
    }

    private IEnumerator PlayPullSoundAfterMeow(int sequenceId)
    {
        if (grabMeow != null)
            yield return new WaitForSeconds(grabMeow.length);

        if (!isDragging || sequenceId != pullSoundSequenceId || pullSound == null)
            yield break;

        AudioManager.Instance?.PlaySfxFromSource(pullSoundSource, pullSound, pullSoundVolume);
    }

    private void StopPullSound()
    {
        pullSoundSequenceId++;
        if (pullSoundSource != null)
            pullSoundSource.Stop();
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
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;
        }

        if (activeAbility.TryActivate())
        {
            abilityUsed = true;
            activeAbility = null;
        }
    }

    private void Aim(Vector2 pointer)
    {
        float fullPullPixels = Mathf.Max(1f, mainCamera.pixelHeight * fullPullScreenFraction);
        Vector2 pull = Vector2.ClampMagnitude(
            (dragStartScreen - pointer) * maxPullDistance / fullPullPixels, maxPullDistance);
        if (pull.sqrMagnitude < 0.001f || pull.x <= 0f)
        {
            launchVelocity = Vector2.zero;
            ResetArm();
            ClearTrajectory();
            return;
        }

        float angle = Mathf.Clamp(Mathf.Atan2(pull.y, pull.x) * Mathf.Rad2Deg,
            minLaunchAngle, Mathf.Max(minLaunchAngle, maxLaunchAngle));
        rotatingPart.localRotation = restingArmRotation
            * Quaternion.Euler(0f, 0f, Mathf.Min(angle, maxArmTilt));
        float radians = angle * Mathf.Deg2Rad;
        launchVelocity = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians))
            * pull.magnitude * launchPower;
        // Загруженный кот не участвует в физике: позиция Transform обновляет спрайт
        // сразу, без задержки интерполяции Rigidbody2D при вращении чаши.
        SetLoadedTransform(launchPoint.position, Quaternion.Euler(0f, 0f, angle));
        float strength = pull.magnitude / Mathf.Max(0.01f, maxPullDistance);
        trajectoryStrength = Mathf.Clamp01(strength);
        trajectoryColor = strength < 0.5f
            ? Color.Lerp(weakPullColor, mediumPullColor, strength * 2f)
            : Color.Lerp(mediumPullColor, strongPullColor, (strength - 0.5f) * 2f);
        PredictTrajectory(projectile.transform.position, launchVelocity, projectile.gravityScale,
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
            / Mathf.Max(1, mainCamera.pixelHeight)
            * Mathf.Lerp(Mathf.Max(0.3f, weakPullDotScale), Mathf.Max(0.3f, strongPullDotScale), trajectoryStrength);
        renderer.transform.position = new Vector3(position.x, position.y, launchPoint.position.z);
        renderer.transform.localScale = Vector3.one * diameter / renderer.sprite.bounds.size.x;
        Color color = trajectoryColor;
        color.a *= Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(visibleDots * dotTimeSpacing
            / (trajectoryPoints * Mathf.Max(0.01f, timeStep))));
        renderer.color = color;
        SpriteRenderer shadow = dotShadows[visibleDots - 1];
        float unitsPerPixel = 2f * mainCamera.orthographicSize / Mathf.Max(1, mainCamera.pixelHeight);
        float horizontalUnitsPerPixel = 2f * mainCamera.orthographicSize * mainCamera.aspect
            / Mathf.Max(1, mainCamera.pixelWidth);
        shadow.transform.position = renderer.transform.position
            + new Vector3(trajectoryShadowOffsetPixels.x * horizontalUnitsPerPixel,
                trajectoryShadowOffsetPixels.y * unitsPerPixel, 0f);
        Color shade = trajectoryShadowColor;
        shade.a *= color.a;
        shadow.color = shade;
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

        Rigidbody2D fired = projectile;
        fired.transform.SetParent(null, true);
        ProjectileImpactReporter reporter = fired.GetComponent<ProjectileImpactReporter>();
        bool impacted = false;
        reporter.Arm(point =>
        {
            impacted = true;
            ProjectileImpacted?.Invoke(point);
        });
        fired.position = fired.transform.position;
        fired.rotation = fired.transform.eulerAngles.z;
        fired.interpolation = RigidbodyInterpolation2D.Interpolate;
        fired.bodyType = RigidbodyType2D.Dynamic;
        fired.simulated = true;
        fired.linearVelocity = launchVelocity;
        activeAbility = fired.GetComponent<CatAbility>();
        if (activeAbility != null)
            activeAbility.Arm();
        shotHadAbility = activeAbility != null;
        flightHintAnchor = fired.transform;
        flightHintPosition = fired.position;
        abilityUsed = false;
        showFlightInstruction = shotHints && shotHadAbility && IsFirstRound && !abilityHintShown;
        if (showFlightInstruction)
        {
            abilityHintShown = true;
            PlayerPrefs.SetInt(AbilityHintsKey, 1);
            PlayerPrefs.Save();
        }
        ShotsFired++;
        launchedProjectiles.Add(fired);
        fired.GetComponent<ProjectileComicEffects>()?.BeginFlight();
        var flightEffects = fired.GetComponent<CatFlightEffects>();
        flightEffects?.BeginFlight(gameplayCamera != null ? gameplayCamera.mapMin.y : -15f, projectileLifetime);
        projectile = null;
        AudioManager.Instance?.PlayShotSound();
        AudioManager.Instance?.PlaySfx(flightMeow, flightMeowVolume);
        ShotLaunched?.Invoke(fired);
        if (flightEffects == null)
            Destroy(fired.gameObject, projectileLifetime);

        ResetArm();
        // Следующий кот занимает чашу, остальные сдвигаются вперёд.
        yield return LoadNextCat();

        float flightDeadline = Time.time + Mathf.Min(projectileLifetime, 7f);
        while (fired != null && !impacted && Time.time < flightDeadline)
            yield return null;
        yield return new WaitForSeconds(spawnDelay);
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
        StopPullSound();
        // Пауза во время выстрела сохраняет уже выбранную скорость запуска.
        if (!shotInProgress)
        {
            launchVelocity = Vector2.zero;
            ResetArm();
        }
        ClearTrajectory();
    }

    public void Reload()
    {
        if (shotInProgress || !CanLoadProjectile || !isActiveAndEnabled)
            return;
        if (projectile != null)
        {
            CancelInput();
            SetLoadedTransform(launchPoint.position, Quaternion.identity);
        }
    }

    private void SetLoadedTransform(Vector3 position, Quaternion rotation)
    {
        // Синхронизирует отключённый Rigidbody2D и видимый Transform.
        projectile.position = position;
        projectile.rotation = rotation.eulerAngles.z;
        projectile.transform.SetPositionAndRotation(position, rotation);
    }

    private void ResetArm()
    {
        rotatingPart.localRotation = restingArmRotation;
        if (projectile != null)
            SetLoadedTransform(launchPoint.position, Quaternion.identity);
    }

    private void CreateShotHints()
    {
        // One reusable, non-interactive overlay; it never catches aiming or ability clicks.
        var root = new GameObject("ShotHintsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        hintCanvas = root.GetComponent<Canvas>();
        hintCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hintCanvas.sortingOrder = 75;
        hintCanvasRect = (RectTransform)root.transform;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        abilityBadge = CreateHintPlate("FirstAbilityHint", new Vector2(290f, 42f));
        abilityBadge.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.96f);
        abilityBadgeLabel = CreateHintLabel(abilityBadge, "Input", 17f);
        abilityBadgeLabel.color = new Color32(244, 235, 221, 255);
        abilityBadgeLabel.text = "ЛКМ / ПРОБЕЛ в полёте";
        cancelHint = CreateHintPlate("CancelPullHint", new Vector2(170f, 32f));
        if (MenuButtonStyle.Default != null)
            cancelHint.GetComponent<Image>().sprite = MenuButtonStyle.Default.lightButtonSprite;
        var cancelLabel = CreateHintLabel(cancelHint, "Input", 16f);
        cancelLabel.text = "ПКМ — отменить";
        cancelLabel.color = new Color32(34, 27, 49, 255);
        abilityBadge.gameObject.SetActive(false);
        cancelHint.gameObject.SetActive(false);
    }

    private RectTransform CreateHintPlate(string name, Vector2 size)
    {
        var plate = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        plate.transform.SetParent(hintCanvasRect, false);
        var rect = (RectTransform)plate.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        var image = plate.GetComponent<Image>();
        image.sprite = MenuButtonStyle.Default != null ? MenuButtonStyle.Default.secondaryButtonSprite : null;
        image.type = Image.Type.Sliced;
        image.color = new Color(1f, 1f, 1f, 0.96f);
        image.raycastTarget = false;
        return rect;
    }

    private static TMP_Text CreateHintLabel(RectTransform parent, string name, float fontSize)
    {
        var label = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        label.transform.SetParent(parent, false);
        var text = label.GetComponent<TextMeshProUGUI>();
        text.font = MenuButtonStyle.Default != null ? MenuButtonStyle.Default.font : TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.color = new Color32(244, 235, 221, 255);
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(6f, 4f);
        text.rectTransform.offsetMax = new Vector2(-6f, -4f);
        return text;
    }

    private void LateUpdate()
    {
        if (hintCanvas == null) return;
        bool visible = shotHints && isActiveAndEnabled && Time.timeScale > 0f
            && !SceneTransition.IsLoading && !FirstLevelGuide.IsOpen && mainCamera != null
            && (levelSession == null || !levelSession.IsEnding);
        bool inFlight = shotInProgress && shotHadAbility;
        CatAbility loadedAbility = !shotInProgress && projectile != null ? projectile.GetComponent<CatAbility>() : null;
        bool showBadge = visible && IsFirstRound && !isDragging
            && (inFlight ? showFlightInstruction && !abilityUsed && activeAbility != null && activeAbility.CanActivate
                : CanAim && loadedAbility != null && !abilityHintShown);
        abilityBadge.gameObject.SetActive(showBadge);
        cancelHint.gameObject.SetActive(visible && isDragging);
        if (showBadge)
        {
            if (inFlight && flightHintAnchor != null) flightHintPosition = flightHintAnchor.position;
            PositionHint(abilityBadge, abilityBadgeOffset, inFlight ? flightHintPosition : launchPoint.position);
        }
        if (visible && isDragging) PositionHint(cancelHint, cancelHintOffset, launchPoint.position);
    }

    private void PositionHint(RectTransform hint, Vector2 offset, Vector3 worldPosition)
    {
        Vector3 screen = mainCamera.WorldToScreenPoint(worldPosition);
        Camera uiCamera = hintCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : hintCanvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(hintCanvasRect, screen, uiCamera, out Vector2 point);
        Vector2 margin = hint.sizeDelta * 0.5f + Vector2.one * 16f;
        Vector2 half = hintCanvasRect.rect.size * 0.5f;
        Vector2 desired = point + offset;
        hint.anchoredPosition = new Vector2(Mathf.Clamp(desired.x, -half.x + margin.x, half.x - margin.x),
            Mathf.Clamp(desired.y, -half.y + margin.y, half.y - margin.y));
    }

    private void OnDisable()
    {
        if (hintCanvas != null) hintCanvas.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (hintCanvas != null) hintCanvas.gameObject.SetActive(true);
    }

    [ContextMenu("Сбросить подсказки способностей")]
    public void ResetAbilityHints()
    {
        abilityHintShown = false;
        PlayerPrefs.DeleteKey(AbilityHintsKey);
        PlayerPrefs.DeleteKey("BPG.Guide.AbilityShots.v1");
        PlayerPrefs.Save();
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
