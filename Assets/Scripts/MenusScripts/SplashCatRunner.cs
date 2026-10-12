using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Бегущий кот под надписью "BP GAMES" на заставке StartupSplash (Boot-сцена).
// Кадры анимации берутся из Assets/Resources/Models/котс_заставка.png — 8 кадров бега,
// нарезанных в самом Unity (см. spriteSheet в котс_заставка.png.meta), поэтому загрузка
// идёт через Resources.LoadAll<Sprite> и не зависит от состояния Git LFS в репозитории.
// Если PNG не загрузился, кот просто не показывается — заставка работает как раньше.
public sealed class SplashCatRunner : MonoBehaviour
{
    private const string SheetPath = "Models/котс_заставка";
    private const int FrameCount = 8;

    [Header("Ссылки")]
    [SerializeField] private StartupController owner; // StartupController этого же префаба

    [Header("Внешний вид")]
    [SerializeField] private float catHeightPx = 96f;   // высота кота в пикселях референсного канваса (1920x1080)
    [SerializeField] private float baselineY = -150f;   // вертикаль "земли" под текстом BP GAMES

    [Header("Бег")]
    [SerializeField] private float minRunDuration = 3f; // минимальная длительность забега, сек
    [SerializeField] private int fps = 12;              // частота смены кадров лап
    [SerializeField] private float bobAmplitude = 4f;   // амплитуда покачивания при беге

    private CanvasGroup group;
    private RectTransform rootRect;
    private RectTransform catRect;
    private Image catImage;
    private Sprite[] frames;

    private void Awake()
    {
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        rootRect = transform as RectTransform;

        LoadFrames();
        if (frames == null)
            return;

        // Трек на всю ширину канваса + маска, чтобы кот въезжал/выезжал строго за края экрана.
        var trackGO = new GameObject("CatTrack", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        var trackRect = trackGO.GetComponent<RectTransform>();
        trackRect.anchorMin = Vector2.zero;
        trackRect.anchorMax = Vector2.one;
        trackRect.offsetMin = Vector2.zero;
        trackRect.offsetMax = Vector2.zero;
        trackGO.GetComponent<Image>().color = Color.clear; // трек невидим, только клипует кота
        trackGO.transform.SetParent(transform, false);

        var catGO = new GameObject("RunningCat", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        catImage = catGO.GetComponent<Image>();
        catImage.raycastTarget = false;
        catImage.preserveAspect = true;

        catRect = catGO.GetComponent<RectTransform>();
        catRect.anchorMin = catRect.anchorMax = new Vector2(0.5f, 0.5f);
        catRect.pivot = new Vector2(0.5f, 0f); // опора по центру лап — покачивание выглядит как бег
        catRect.sizeDelta = new Vector2(catHeightPx * 1.7f, catHeightPx);
        catRect.anchoredPosition = new Vector2(-9999f, baselineY);
        catRect.SetParent(trackRect, false);
    }

    private void Start()
    {
        if (frames != null)
            StartCoroutine(RunLoop());
    }

    private void LoadFrames()
    {
        Texture2D sheet = Resources.Load<Texture2D>(SheetPath);
        if (sheet == null)
        {
            Debug.LogWarning($"SplashCatRunner: спрайтшит '{SheetPath}' не найден, бегущий кот пропущен.");
            return;
        }
        // Пиксель-арт: без линейной фильтрации кадры будут чёткими.
        sheet.filterMode = FilterMode.Point;

        // Берём уже нарезанные Unity суб-ассеты Sprite по именам кадров из .meta.
        var all = Resources.LoadAll<Sprite>(SheetPath);
        frames = new Sprite[FrameCount];
        for (int i = 0; i < FrameCount; i++)
        {
            frames[i] = System.Array.Find(all, s => s.name == $"котс_заставка_{i}");
            if (frames[i] == null)
            {
                Debug.LogWarning($"SplashCatRunner: кадр 'котс_заставка_{i}' не найден, бегущий кот пропущен.");
                frames = null;
                return;
            }
        }
    }

    private IEnumerator RunLoop()
    {
        // Стартуем синхронно с проявлением бренда (fadeIn у StartupController).
        while (owner != null && !owner.IsBrandingVisible())
            yield return null;
        group.alpha = 1f;

        // Ровно один осмысленный забег: старт сразу после проявления логотипа,
        // финиш — к концу видимой части заставки (hold + начало fadeOut).
        float visibleTime = owner != null ? owner.GetBrandingVisibleDuration() : minRunDuration;
        float duration = Mathf.Max(minRunDuration, visibleTime - 0.2f);
        yield return RunOnce(duration);
    }

    private IEnumerator RunOnce(float duration)
    {
        float halfWidth = GetHalfCanvasWidth();
        float margin = catRect.sizeDelta.x; // полный выход кота за край экрана
        float startX = -halfWidth - margin;
        float endX = halfWidth + margin;
        double started = Time.realtimeSinceStartupAsDouble;
        float phase = 0f;

        while (true)
        {
            float elapsed = (float)(Time.realtimeSinceStartupAsDouble - started);
            if (elapsed >= duration || owner == null || !owner.isActiveAndEnabled)
                break;

            // Равномерный горизонтальный бег слева направо.
            float t = Mathf.Clamp01(elapsed / duration);
            float x = Mathf.Lerp(startX, endX, t);

            // Лёгкое покачивание вверх-вниз в такт смене кадров лап.
            phase += Time.unscaledDeltaTime * fps * Mathf.PI;
            float y = baselineY + Mathf.Sin(phase) * bobAmplitude;
            catRect.anchoredPosition = new Vector2(x, y);

            int frame = Mathf.Min((int)(elapsed * fps) % FrameCount, FrameCount - 1);
            if (catImage.sprite != frames[frame])
                catImage.sprite = frames[frame];

            yield return null;
        }
    }

    private float GetHalfCanvasWidth()
    {
        // Ориентируемся на reference resolution CanvasScaler (1920x1080):
        // позиционируем кота в координатах канваса, а не реального экрана.
        var scaler = GetComponentInParent<CanvasScaler>();
        if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            return scaler.referenceResolution.x * 0.5f;
        return rootRect != null ? rootRect.rect.width * 0.5f : 960f;
    }
}
