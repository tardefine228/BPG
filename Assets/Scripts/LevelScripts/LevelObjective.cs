using System;
using UnityEngine;
using UnityEngine.Events;

// Универсальная НЕвражеская цель: кнопка, кристалл, рычаг, предмет для сбора.
// При необходимости вызывай Complete() из другого скрипта или UnityEvent.
[DisallowMultipleComponent]
public sealed class LevelObjective : MonoBehaviour
{
    public enum ActivationMode
    {
        Manual,
        CatCollision,
        CatTrigger
    }

    [SerializeField] private bool countsForVictory = true;
    [SerializeField] private ActivationMode activation = ActivationMode.CatCollision;
    [SerializeField, Min(0)] private int completionPoints = 400;
    [SerializeField] private UnityEvent onCompleted = new UnityEvent();

    public bool CountsForVictory => countsForVictory;
    public bool IsCompleted { get; private set; }
    public int CompletionPoints => completionPoints;
    public event Action<LevelObjective> Completed;

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (activation == ActivationMode.CatCollision && collision != null
            && collision.gameObject.GetComponentInParent<CatProjectile>() != null)
            Complete();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (activation == ActivationMode.CatTrigger && other != null
            && other.GetComponentInParent<CatProjectile>() != null)
            Complete();
    }

    public void Complete()
    {
        if (IsCompleted)
            return;
        IsCompleted = true;
        Completed?.Invoke(this);
        onCompleted.Invoke();
    }
}
