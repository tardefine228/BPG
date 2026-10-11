using UnityEngine;

// Рывок вперёд. Повторный ЛКМ / пробел в полёте.
public sealed class DashCatAbility : CatAbility
{
    [SerializeField, Min(0f)] private float boostImpulse = 18f;

    protected override void Activate()
    {
        Vector2 direction = Body.linearVelocity.sqrMagnitude > 0.01f
            ? Body.linearVelocity.normalized : (Vector2)transform.right;
        Body.AddForce(direction * boostImpulse, ForceMode2D.Impulse);
    }
}
