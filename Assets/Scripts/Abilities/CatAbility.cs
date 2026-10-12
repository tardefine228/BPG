using UnityEngine;

// Базовый класс способностей. НА PREFAB добавляй не его, а конкретную способность.
[RequireComponent(typeof(Rigidbody2D))]
public abstract class CatAbility : MonoBehaviour
{
    protected Rigidbody2D Body { get; private set; }

    private bool armed;
    private bool activated;
    private bool touchedSomething;

    public bool CanActivate => armed && !activated && !touchedSomething
        && Body != null && Body.simulated && Body.bodyType == RigidbodyType2D.Dynamic;

    protected virtual void Awake()
    {
        Body = GetComponent<Rigidbody2D>();
    }

    // Вызывается SlingshotController строго после настоящего выстрела.
    public void Arm()
    {
        armed = true;
        activated = false;
        touchedSomething = false;
    }

    public bool TryActivate()
    {
        if (!CanActivate)
            return false;

        activated = true;
        Activate();
        return true;
    }

    protected abstract void Activate();

    // Нельзя использовать способность после первого контакта с препятствием.
    protected virtual void OnCollisionEnter2D(Collision2D collision)
    {
        if (armed)
            touchedSomething = true;
    }
}
