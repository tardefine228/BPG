using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Вешается на звезду карточки уровня: при наведении курсора звезда переворачивается
// вокруг вертикальной оси, а в середине вращения спрайт меняется на противоположный.
// Пороги звёзд (LevelProgress) не затрагиваются — это только визуальный эффект.
[DisallowMultipleComponent]
public sealed class StarFlipOnHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField, Min(0.05f)] private float duration = 0.3f;
    [SerializeField] private Sprite starOnSprite;
    [SerializeField] private Sprite starOffSprite;

    private Image starImage;
    private RectTransform rect;
    private Coroutine flipRoutine;
    private Quaternion restRotation = Quaternion.identity;

    private void Awake()
    {
        starImage = GetComponent<Image>();
        rect = (RectTransform)transform;
        restRotation = rect.localRotation;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (flipRoutine != null)
            StopCoroutine(flipRoutine);
        flipRoutine = StartCoroutine(FlipRoutine());
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // Указатель ушёл до конца анимации — возвращаем исходный поворот.
        if (flipRoutine == null)
            rect.localRotation = restRotation;
    }

    private IEnumerator FlipRoutine()
    {
        Quaternion start = rect.localRotation;
        Quaternion halfTurn = restRotation * Quaternion.Euler(0f, 180f, 0f);
        bool swapped = false;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            rect.localRotation = Quaternion.Slerp(start, halfTurn, t);
            if (!swapped && t >= 0.5f)
            {
                swapped = true;
                SwapSprite();
            }
            yield return null;
        }

        // Доворот до «изнанки» и возврат к лицевой стороне с той же подменой спрайта.
        Quaternion backTarget = restRotation;
        elapsed = 0f;
        Quaternion from = rect.localRotation;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            rect.localRotation = Quaternion.Slerp(from, backTarget, t);
            if (swapped && t >= 0.5f)
            {
                swapped = false;
                SwapSprite();
            }
            yield return null;
        }

        rect.localRotation = restRotation;
        if (swapped)
            SwapSprite();
        flipRoutine = null;
    }

    private void SwapSprite()
    {
        if (starImage == null || starOnSprite == null || starOffSprite == null)
            return;
        starImage.sprite = starImage.sprite == starOnSprite ? starOffSprite : starOnSprite;
    }
}
