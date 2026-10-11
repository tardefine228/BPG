using UnityEngine;

// В полёте кот становится тяжелее, сохраняя скорость: сильнее толкает блоки.
public sealed class HeavyCatAbility : CatAbility
{
    [SerializeField, Min(1f)] private float massMultiplier = 4f;
    [SerializeField, Min(0f)] private float forwardImpulse = 3f;

    protected override void Activate()
    {
        Body.mass *= massMultiplier;
        if (Body.linearVelocity.sqrMagnitude > 0.01f)
            Body.AddForce(Body.linearVelocity.normalized * forwardImpulse, ForceMode2D.Impulse);
    }
}
