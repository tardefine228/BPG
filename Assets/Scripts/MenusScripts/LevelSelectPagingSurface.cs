using System;
using UnityEngine;
using UnityEngine.EventSystems;

// Передаёт свайпы и колесо мыши контроллеру страниц, включая жесты над дочерними карточками.
[DisallowMultipleComponent]
public sealed class LevelSelectPagingSurface : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    // Смещение указателя по горизонтали в пикселях экрана.
    public event Action<float> Dragged;

    // Событие завершения перетаскивания.
    public event Action DragCompleted;

    // Смещение колеса мыши; положительное значение означает прокрутку вверх.
    public event Action<float> Scrolled;

    public void OnBeginDrag(PointerEventData eventData)
    {
        // Контроллер читает текущее положение страниц без предварительной подготовки.
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData != null && Dragged != null)
        {
            Dragged(eventData.delta.x);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (DragCompleted != null)
        {
            DragCompleted();
        }
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (eventData != null && Scrolled != null)
        {
            Scrolled(eventData.scrollDelta.y);
        }
    }
}
