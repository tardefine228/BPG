using UnityEngine;

// Swaps only sprites; colliders, transforms and game state stay under the gameplay scripts.
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class IdleSpriteAnimation : MonoBehaviour
{
    [Header("Кадры анимации ожидания")]
    [Tooltip("Перетащите спрайты в порядке воспроизведения. Пустой список оставляет обычный вид префаба.")]
    [SerializeField] private Sprite[] frames = new Sprite[0];
    [SerializeField, Range(1f, 30f)] private float framesPerSecond = 6f;
    [Tooltip("Прямой порядок, затем обратный. Крайние кадры не дублируются.")]
    [SerializeField] private bool pingPong;
    [Tooltip("Разная начальная фаза у экземпляров одного префаба.")]
    [SerializeField] private bool variedStart = true;
    [Header("Мыши анимируются только на месте")]
    [SerializeField] private bool onlyWhenStill = true;
    [SerializeField, Min(0f)] private float stillSpeed = 0.2f;
    [SerializeField, Min(0f)] private float stillAngularSpeed = 8f;

    private SpriteRenderer spriteRenderer;
    private CatProjectile cat;
    private MouseEnemy mouse;
    private Rigidbody2D body;
    private Sprite restSprite;
    private Sprite displayedFrame;
    private float elapsed;
    private int phase;
    private bool playing;

    public Sprite[] Frames => frames;
    public float FramesPerSecond => framesPerSecond;
    public bool PingPong => pingPong;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        cat = GetComponent<CatProjectile>();
        mouse = GetComponent<MouseEnemy>();
        body = GetComponent<Rigidbody2D>();
        restSprite = spriteRenderer.sprite;
        // Do not consume UnityEngine.Random: this should not affect shots, sounds or physics.
        phase = variedStart ? (GetEntityId().GetHashCode() & int.MaxValue) : 0;
    }

    public void SetRestPose(Sprite sprite)
    {
        restSprite = sprite;
        displayedFrame = null;
        elapsed = 0f;
        playing = false;
    }

    private void LateUpdate()
    {
        bool allowed = frames != null && frames.Length > 0 && spriteRenderer != null
            && spriteRenderer.enabled && (cat == null || cat.IsQueued)
            && (mouse == null || !mouse.IsDead);
        if (allowed && cat == null && onlyWhenStill && body != null)
            allowed = body.linearVelocity.sqrMagnitude <= stillSpeed * stillSpeed
                && Mathf.Abs(body.angularVelocity) <= stillAngularSpeed;
        if (!allowed)
        {
            StopAnimation();
            return;
        }
        // Freeze on the same frame during tutorial, pause and scene transitions.
        if (Time.deltaTime <= 0f) return;
        if (!playing)
        {
            restSprite = spriteRenderer.sprite;
            playing = true;
        }
        elapsed += Time.deltaTime;
        int count = frames.Length;
        int cycle = pingPong && count > 1 ? count * 2 - 2 : count;
        int step = (phase % cycle + Mathf.FloorToInt(elapsed * framesPerSecond) % cycle) % cycle;
        int index = pingPong && step >= count ? cycle - step : step;
        Sprite frame = frames[index];
        if (frame == null) return;
        spriteRenderer.sprite = frame;
        displayedFrame = frame;
    }

    private void StopAnimation()
    {
        // A launch/death script may have already assigned its own sprite this frame.
        if (playing && spriteRenderer != null && spriteRenderer.sprite == displayedFrame
            && (mouse == null || !mouse.IsDead))
            spriteRenderer.sprite = restSprite;
        playing = false;
        displayedFrame = null;
        elapsed = 0f;
    }

    private void OnDisable() => StopAnimation();
}
