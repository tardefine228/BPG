using UnityEngine;

// Умножение: по ЛКМ в полёте кот разлетается на несколько копий.
// Копии летят веером в текущем направлении, каждая бьёт как обычный снаряд.
public sealed class MultiplyCatAbility : CatAbility
{
    [SerializeField, Min(1)] private int totalCats = 3;
    [SerializeField, Range(0f, 90f)] private float fanAngle = 20f;
    [SerializeField, Min(0f)] private float forwardImpulse = 4f;
    [SerializeField, Min(0.1f)] private float copyLifetime = 2.5f;

    protected override void Activate()
    {
        Vector2 velocity = Body.linearVelocity;
        Vector2 direction = velocity.sqrMagnitude > 0.01f
            ? velocity.normalized : (Vector2)transform.right;
        float speed = velocity.magnitude;

        // Оригинальный кот остаётся центральным, копии разлетаются веером.
        for (int i = 0; i < totalCats - 1; i++)
        {
            int side = (i % 2 == 0) ? 1 : -1;
            float angleDeg = fanAngle * (0.5f + i / 2f) * side;
            Vector2 spread = Quaternion.Euler(0f, 0f, angleDeg) * direction;

            GameObject copy = Instantiate(gameObject, transform.position, transform.rotation);
            Rigidbody2D copyBody = copy.GetComponent<Rigidbody2D>();
            copyBody.bodyType = RigidbodyType2D.Dynamic;
            copyBody.simulated = true;
            copyBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            copyBody.linearVelocity = spread * speed;
            copyBody.AddForce(spread * forwardImpulse, ForceMode2D.Impulse);

            // Способность одноразовая: копия не должна умножаться повторно.
            CatAbility copyAbility = copy.GetComponent<CatAbility>();
            if (copyAbility != null)
                Destroy(copyAbility);

            // Автоудаление копии даже после уничтожения оригинала (Destroy
            // переживает смерть самого GameObject кота).
            Destroy(copy, copyLifetime);
        }
    }
}
