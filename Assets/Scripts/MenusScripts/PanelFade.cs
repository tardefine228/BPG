using System.Collections;
using UnityEngine;

/// <summary>Shared menu fade, including when gameplay is paused.</summary>
[RequireComponent(typeof(CanvasGroup))]
public sealed class PanelFade : MonoBehaviour
{
    [Min(0f)] public float duration = 0.18f;
    private CanvasGroup group;
    private Coroutine routine;
    public bool IsVisible { get; private set; }

    public void SetVisible(bool visible, bool immediate = false)
    {
        if (group == null)
            group = GetComponent<CanvasGroup>();
        if (routine != null)
            StopCoroutine(routine);
        routine = null;
        IsVisible = visible;
        group.interactable = visible;
        // A closing panel continues to absorb clicks until it disappears.
        group.blocksRaycasts = true;
        if (visible)
            gameObject.SetActive(true);
        if (immediate || duration <= 0f || !gameObject.activeInHierarchy)
        {
            Finish(visible);
            return;
        }
        routine = StartCoroutine(Fade(visible));
    }

    private IEnumerator Fade(bool visible)
    {
        float from = group.alpha;
        float started = Time.unscaledTime;
        while (Time.unscaledTime - started < duration)
        {
            float elapsed = Time.unscaledTime - started;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            group.alpha = Mathf.Lerp(from, visible ? 1f : 0f, t);
            yield return null;
        }
        Finish(visible);
    }

    private void Finish(bool visible)
    {
        routine = null;
        group.alpha = visible ? 1f : 0f;
        group.blocksRaycasts = visible;
        if (!visible)
            gameObject.SetActive(false);
    }
}
