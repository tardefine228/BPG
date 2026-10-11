using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Forwards pointer drags and mouse wheel input from the lobby UI to
/// <see cref="LevelSelectController"/>. The component sits on the lobby root,
/// so drags and scrolls that start on any child (background, level card,
/// arrow) bubble up to it and flip the level pages.
/// </summary>
[DisallowMultipleComponent]
public sealed class LevelSelectPagingSurface : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    /// <summary>Horizontal pointer delta in screen pixels.</summary>
    public event Action<float> Dragged;

    /// <summary>Raised when the pointer is released after a drag.</summary>
    public event Action DragCompleted;

    /// <summary>Mouse wheel delta, positive when scrolling up.</summary>
    public event Action<float> Scrolled;

    public void OnBeginDrag(PointerEventData eventData)
    {
        // Nothing to prepare: the controller reads the live track position.
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
