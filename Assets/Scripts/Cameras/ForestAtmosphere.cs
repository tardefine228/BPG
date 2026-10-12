using UnityEngine;
using UnityEngine.Rendering;

/// <summary>A single procedural world overlay. Screen-space menus are rendered above it.</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(250)]
public sealed class ForestAtmosphere : MonoBehaviour
{
    [Header("Материал атмосферы")]
    [Tooltip("Материал BPG/ForestAtmosphere. Ссылка также сохраняет шейдер в сборке.")]
    public Material atmosphereMaterial;

    [Header("Мягкий лесной туман")]
    public bool fogEnabled = true;
    [Tooltip("Максимальная непрозрачность тумана. 0 — полностью выключить.")]
    [Range(0f, 0.35f)] public float fogOpacity = 0.075f;
    public Color fogColor = new Color(0.91f, 0.96f, 0.9f, 1f);
    [Tooltip("Размер облаков тумана в единицах игрового мира.")]
    [Min(3f)] public float fogWorldScale = 30f;
    [Tooltip("Плотность плавной дымки между широкими струями тумана.")]
    [Range(0f, 1f)] public float fogDensity = 0.72f;
    [Tooltip("Высота нижнего слоя: доля экрана от нижнего края. При этом значении туман уже наполовину растворяется.")]
    [Range(0.05f, 0.9f)] public float fogHeight = 0.35f;
    [Tooltip("Ширина плавного перехода от нижнего тумана к прозрачному небу.")]
    [Range(0.05f, 0.6f)] public float fogHeightFalloff = 0.30f;
    [Tooltip("Направление и скорость движения тумана: единиц мира в секунду.")]
    public Vector2 fogDrift = new Vector2(0.18f, 0.008f);

    [Header("Свет сквозь листву")]
    public bool lightEnabled = true;
    public Color sunlightColor = new Color(1f, 0.94f, 0.77f, 1f);
    [Tooltip("Яркость рассеянного света и нерегулярных мягких лучей. 0 — выключить лучи.")]
    [Range(0f, 0.3f)] public float rayIntensity = 0.035f;
    [Tooltip("Яркость световых пятен на фоне и объектах. 0 — выключить пятна.")]
    [Range(0f, 0.3f)] public float dappleIntensity = 0.018f;
    [Tooltip("Размер нерегулярных просветов в листве и плавных световых пятен в единицах мира.")]
    [Min(3f)] public float lightWorldScale = 21f;
    [Tooltip("Угол падения света от вертикали, в градусах.")]
    [Range(-60f, 60f)] public float rayAngle = 24f;
    [Tooltip("Относительная ширина и мягкость лучей.")]
    [Range(0.15f, 0.85f)] public float rayWidth = 0.72f;
    [Tooltip("Скорость медленного движения и переливания света.")]
    [Range(0f, 2f)] public float lightAnimationSpeed = 0.24f;
    [Tooltip("Сила плавного изменения яркости света.")]
    [Range(0f, 1f)] public float lightShimmer = 0.22f;

    [Header("Общие настройки и предпросмотр")]
    [Tooltip("Множитель скорости всей атмосферы. Пауза игры останавливает анимацию.")]
    [Range(0f, 3f)] public float animationSpeed = 1f;
    [Tooltip("Другой рисунок тумана и света без дополнительных текстур.")]
    public float seed = 17f;
    [Tooltip("Слой отрисовки мира. Интерфейсы Screen Space Overlay всегда остаются сверху.")]
    public int sortingOrder = 60;
    public bool previewInEditMode = true;
    [Tooltip("В редакторе по умолчанию показывается неподвижный предпросмотр.")]
    public bool animateInEditMode;

    public float AnimationTime => animationTime;
    public bool IsOverlayReady => overlayRenderer != null && runtimeMaterial != null;
    public bool IsRendering => overlayRenderer != null && overlayRenderer.enabled;

    private Camera view;
    private GameObject overlay;
    private Mesh overlayMesh;
    private MeshRenderer overlayRenderer;
    private Material runtimeMaterial;
    private Material sourceMaterial;
    private float animationTime;
#if UNITY_EDITOR
    private double lastPreviewTime;
#endif

    private static readonly int FogColorId = Shader.PropertyToID("_FogColor");
    private static readonly int SunlightColorId = Shader.PropertyToID("_SunlightColor");
    private static readonly int FogParamsId = Shader.PropertyToID("_FogParams");
    private static readonly int FogDriftId = Shader.PropertyToID("_FogDrift");
    private static readonly int FogHeightId = Shader.PropertyToID("_FogHeight");
    private static readonly int LightParamsId = Shader.PropertyToID("_LightParams");
    private static readonly int LightMotionId = Shader.PropertyToID("_LightMotion");
    private static readonly int AnimationTimeId = Shader.PropertyToID("_AnimationTime");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");

    private void OnEnable()
    {
        view = GetComponent<Camera>();
#if UNITY_EDITOR
        lastPreviewTime = UnityEditor.EditorApplication.timeSinceStartup;
#endif
        UpdateOverlay();
    }

    private void LateUpdate()
    {
        if (Application.IsPlaying(gameObject))
            animationTime += Time.deltaTime * Mathf.Max(0f, animationSpeed);
#if UNITY_EDITOR
        else
        {
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            if (animateInEditMode && previewInEditMode)
            {
                animationTime += Mathf.Min(0.1f, (float)(now - lastPreviewTime))
                    * Mathf.Max(0f, animationSpeed);
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditor.SceneView.RepaintAll();
            }
            lastPreviewTime = now;
        }
#endif
        UpdateOverlay();
    }

    private void UpdateOverlay()
    {
        if (view == null)
            view = GetComponent<Camera>();

        bool visible = view != null && view.enabled && atmosphereMaterial != null
            && (Application.IsPlaying(gameObject) || previewInEditMode)
            && ((fogEnabled && fogOpacity > 0f)
                || (lightEnabled && (rayIntensity > 0f || dappleIntensity > 0f)));
        if (!visible)
        {
            if (overlayRenderer != null)
                overlayRenderer.enabled = false;
            return;
        }

        if (overlay == null || runtimeMaterial == null || sourceMaterial != atmosphereMaterial)
        {
            ReleaseResources();
            CreateOverlay();
        }

        // This quad follows the camera frame, but shader noise stays anchored in world space.
        float distance = Mathf.Max(0.5f, view.nearClipPlane + 0.1f);
        float height = view.orthographic ? view.orthographicSize * 2f
            : 2f * distance * Mathf.Tan(view.fieldOfView * Mathf.Deg2Rad * 0.5f);
        overlay.transform.localPosition = new Vector3(0f, 0f, distance);
        overlay.transform.localRotation = Quaternion.identity;
        overlay.transform.localScale = new Vector3(height * view.aspect * 1.01f, height * 1.01f, 1f);
        overlay.layer = gameObject.layer;
        overlayRenderer.sortingOrder = sortingOrder;
        overlayRenderer.enabled = true;

        runtimeMaterial.SetColor(FogColorId, fogColor);
        runtimeMaterial.SetColor(SunlightColorId, sunlightColor);
        runtimeMaterial.SetVector(FogParamsId, new Vector4(
            fogEnabled ? Mathf.Clamp(fogOpacity, 0f, 0.35f) : 0f,
            1f / Mathf.Max(3f, fogWorldScale), Mathf.Clamp01(fogDensity), 0f));
        runtimeMaterial.SetVector(FogDriftId, new Vector4(fogDrift.x, fogDrift.y, 0f, 0f));
        runtimeMaterial.SetVector(FogHeightId, new Vector4(
            Mathf.Clamp(fogHeight, 0.05f, 0.9f), Mathf.Clamp(fogHeightFalloff, 0.05f, 0.6f), 0f, 0f));
        runtimeMaterial.SetVector(LightParamsId, new Vector4(
            lightEnabled ? Mathf.Clamp(rayIntensity, 0f, 0.3f) : 0f,
            lightEnabled ? Mathf.Clamp(dappleIntensity, 0f, 0.3f) : 0f,
            1f / Mathf.Max(3f, lightWorldScale),
            Mathf.Tan(Mathf.Clamp(rayAngle, -60f, 60f) * Mathf.Deg2Rad)));
        runtimeMaterial.SetVector(LightMotionId, new Vector4(
            Mathf.Clamp(rayWidth, 0.15f, 0.85f), Mathf.Clamp(lightAnimationSpeed, 0f, 2f),
            Mathf.Clamp01(lightShimmer), 0f));
        runtimeMaterial.SetFloat(AnimationTimeId, animationTime);
        runtimeMaterial.SetFloat(SeedId, seed);
    }

    private void CreateOverlay()
    {
        sourceMaterial = atmosphereMaterial;
        runtimeMaterial = new Material(atmosphereMaterial)
        {
            name = "Forest atmosphere (camera instance)",
            hideFlags = HideFlags.HideAndDontSave
        };
        overlayMesh = new Mesh
        {
            name = "Forest atmosphere quad",
            hideFlags = HideFlags.HideAndDontSave,
            vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
            },
            uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
            triangles = new[] { 0, 2, 1, 2, 3, 1 }
        };
        overlayMesh.RecalculateBounds();

        overlay = new GameObject("Forest atmosphere (preview/runtime)")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        overlay.transform.SetParent(transform, false);
        overlay.AddComponent<MeshFilter>().sharedMesh = overlayMesh;
        overlayRenderer = overlay.AddComponent<MeshRenderer>();
        overlayRenderer.sharedMaterial = runtimeMaterial;
        overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
        overlayRenderer.receiveShadows = false;
        overlayRenderer.lightProbeUsage = LightProbeUsage.Off;
        overlayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        overlayRenderer.allowOcclusionWhenDynamic = false;
    }

    private void OnDisable() => ReleaseResources();
    private void OnDestroy() => ReleaseResources();

    private void ReleaseResources()
    {
        DestroyGenerated(overlay);
        DestroyGenerated(overlayMesh);
        DestroyGenerated(runtimeMaterial);
        overlay = null;
        overlayMesh = null;
        overlayRenderer = null;
        runtimeMaterial = null;
        sourceMaterial = null;
    }

    private static void DestroyGenerated(Object generated)
    {
        if (generated == null)
            return;
        if (Application.isPlaying)
            Destroy(generated);
        else
            DestroyImmediate(generated);
    }
}
