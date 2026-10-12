using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Camera))]
[ExecuteAlways]
public sealed class GameplayCameraController : MonoBehaviour
{
    [Header("Вся карта в одном кадре")]
    public SpriteRenderer ground;
    public SpriteRenderer background;
    public Vector2 mapMin = new Vector2(-39f, -7.5f);
    public Vector2 mapMax = new Vector2(39f, 29f);
    [Min(0f)] public float overviewPadding = 1.5f;

    [Header("Приближение при входе в уровень")]
    [Tooltip("Насколько дальше камера в начале. 0 — без приближения, 0.12 — на 12% дальше.")]
    [Range(0f, 0.35f)] public float introZoom = 0.12f;
    [Range(0.2f, 3f)] public float introDuration = 1.2f;

    public bool IsBusy => introPlaying;
    private Camera view;
    private float introProgress;
    private bool introPlaying;
    private float lastAspect;

    private float GameplaySize => Mathf.Max((mapMax.y - mapMin.y) * 0.5f,
        (mapMax.x - mapMin.x) / (2f * Mathf.Max(0.1f, view.aspect))) + overviewPadding;

    private void Awake()
    {
        view = GetComponent<Camera>();
        view.orthographic = true;
        introPlaying = Application.IsPlaying(gameObject) && introZoom > 0f;
        introProgress = Application.IsPlaying(gameObject) ? 0f : 1f;
        ApplyFrame();
        FitBackdrop();
        lastAspect = view.aspect;
    }

    private IEnumerator Start()
    {
        if (!Application.IsPlaying(gameObject))
            yield break;
        // Приближение начинается после загрузки и закрытия обучения.
        while (SceneTransition.IsLoading || Time.timeScale <= 0f)
            yield return null;
        while (introPlaying && introProgress < 1f)
        {
            if (Time.timeScale > 0f)
                introProgress = Mathf.Clamp01(introProgress + Time.deltaTime / Mathf.Max(0.2f, introDuration));
            ApplyFrame();
            yield return null;
        }
        introPlaying = false;
        introProgress = 1f;
        ApplyFrame();
    }

    private void LateUpdate()
    {
        if (view == null)
            view = GetComponent<Camera>();
        if (!Application.IsPlaying(gameObject))
        {
            introProgress = 1f;
            ApplyFrame();
        }
        if (Mathf.Approximately(lastAspect, view.aspect))
            return;
        ApplyFrame();
        FitBackdrop();
        lastAspect = view.aspect;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
        {
            UnityEditor.EditorApplication.delayCall -= PreviewFrame;
            UnityEditor.EditorApplication.delayCall += PreviewFrame;
        }
    }

    private void PreviewFrame()
    {
        if (this == null || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        view = GetComponent<Camera>();
        view.orthographic = true;
        introProgress = 1f;
        ApplyFrame();
        FitBackdrop();
    }
#endif

    private void ApplyFrame()
    {
        Vector2 center = (mapMin + mapMax) * 0.5f;
        transform.position = new Vector3(center.x, center.y, transform.position.z);
        view.orthographicSize = Mathf.Max(0.1f, GameplaySize)
            * Mathf.Lerp(1f + introZoom, 1f, Mathf.SmoothStep(0f, 1f, introProgress));
    }

    private void FitBackdrop()
    {
        float size = GameplaySize * (1f + introZoom);
        Vector2 center = (mapMin + mapMax) * 0.5f;
        float left = center.x - size * view.aspect - 8f;
        float right = center.x + size * view.aspect + 8f;
        float bottom = center.y - size - 8f;
        float top = center.y + size + 8f;
        if (ground != null && ground.sprite != null)
        {
            // Сохраняет высоту физического пола при подгонке краёв кадра.
            Bounds bounds = ground.bounds;
            left = Mathf.Min(left, bounds.min.x);
            right = Mathf.Max(right, bounds.max.x);
            bottom = Mathf.Min(bottom, bounds.min.y);
            ground.transform.position = new Vector3((left + right) * 0.5f,
                (bounds.max.y + bottom) * 0.5f, ground.transform.position.z);
            Vector3 spriteSize = ground.sprite.bounds.size;
            ground.transform.localScale = new Vector3((right - left) / spriteSize.x,
                (bounds.max.y - bottom) / spriteSize.y, 1f);
        }
        if (background != null)
        {
            background.transform.position = new Vector3((left + right) * 0.5f,
                (bottom + top) * 0.5f, background.transform.position.z);
            background.size = new Vector2(right - left, top - bottom);
        }
    }
}
