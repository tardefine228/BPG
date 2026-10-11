using UnityEngine;

// Один кот сохраняет свой вид при переходе из очереди в катапульту и в полёт.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer))]
public sealed class CatProjectile : MonoBehaviour
{
    [Header("Спрайты этого вида кота")]
    [Tooltip("Кот, стоящий в очереди позади катапульты.")]
    [SerializeField] private Sprite queueSprite;
    [Tooltip("Один и тот же спрайт для кота в катапульте и в полёте.")]
    [SerializeField] private Sprite launchSprite;
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Sprite placeholderSprite;
    public bool IsQueued { get; private set; }

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        placeholderSprite = spriteRenderer.sprite;
    }

    public void ShowQueued() => SetPose(true);
    public void ShowLoaded() => SetPose(false);

    private void SetPose(bool queued)
    {
        IsQueued = queued;
        Sprite pose = queued ? queueSprite : launchSprite;
        // Пока художник не назначил текстуры, виден спрайт-заглушка из prefab.
        spriteRenderer.sprite = pose != null ? pose : placeholderSprite;
    }
}
