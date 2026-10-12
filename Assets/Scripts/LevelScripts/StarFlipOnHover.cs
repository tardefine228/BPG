using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Decorative one-turn flip. Never changes the earned-star sprite.
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class StarFlipOnHover : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float duration = 0.6f;

    private RectTransform rect;
    private Vector3 originalScale;
    private Coroutine flipRoutine;

    private void Awake()
    {
        rect = (RectTransform)transform;
        originalScale = rect.localScale;
    }

    public void PlayFlip(float delay = 0f)
    {
        if (rect == null)
            Awake();

        if (flipRoutine != null)
            StopCoroutine(flipRoutine);
        rect.localScale = originalScale;
        flipRoutine = StartCoroutine(Flip(delay));
    }

    public void ResetFlip()
    {
        if (flipRoutine != null)
            StopCoroutine(flipRoutine);
        flipRoutine = null;
        if (rect != null)
            rect.localScale = originalScale;
    }

    private IEnumerator Flip(float delay)
    {
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // 360 degree flip around vertical axis, visible with Screen Space UI.
            float horizontalScale = Mathf.Cos(t * Mathf.PI * 2f);
            rect.localScale = new Vector3(originalScale.x * horizontalScale,
                originalScale.y, originalScale.z);
            yield return null;
        }
        rect.localScale = originalScale;
        flipRoutine = null;
    }

    private void OnDisable() => ResetFlip();
}
