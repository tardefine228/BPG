using UnityEngine;

// Умножение: по ЛКМ в полёте кот разлетается на несколько копий.
// Копии летят веером в текущем направлении, каждая бьёт как обычный снаряд.
public sealed class MultiplyCatAbility : CatAbility
{
    [SerializeField, Min(1)] private int totalCats = 3;
    [SerializeField, Range(0f, 90f)] private float fanAngle = 20f;
    [SerializeField, Min(0f)] private float forwardImpulse = 4f;
    [SerializeField, Min(0.1f)] private float copyLifetime = 2.5f;
    [SerializeField] private LayerMask solidLayers = ~0;

    protected override void Activate()
    {
        Vector2 velocity = Body.linearVelocity;
        Vector2 direction = velocity.sqrMagnitude > 0.01f
            ? velocity.normalized : (Vector2)transform.right;
        float speed = Mathf.Max(velocity.magnitude, 1f);

        // Радиус «зоны вылета»: чтобы копия не рождалась внутри коллайдера
        // оригинала (иначе физикаResolve столкнет их и гасит скорость).
        Collider2D selfCollider = GetComponent<Collider2D>();
        float spawnRadius = selfCollider != null
            ? selfCollider.bounds.extents.magnitude + 0.1f
            : 0.5f;

        // Оригинальный кот остаётся центральным, копии разлетаются веером.
        for (int i = 0; i < totalCats - 1; i++)
        {
            int side = (i % 2 == 0) ? 1 : -1;
            float angleDeg = fanAngle * (0.5f + i / 2f) * side;
            Vector2 spread = Quaternion.Euler(0f, 0f, angleDeg) * direction;

            // Точка спавна: чуть вперёд по направлению полёта и в сторону
            // веера, за пределы собственного коллайдера.
            Vector2 spawnPos = transform.position
                + spread * spawnRadius
                + direction * (spawnRadius * 0.5f);

            // Не спавним копию внутри стен/блоков — иначе она мгновенно
            // застрянет и остановится.
            if (selfCollider != null &&
                Physics2D.OverlapPoint(spawnPos, solidLayers))
            {
                spawnPos = transform.position + direction * (spawnRadius * 1.5f);
                if (Physics2D.OverlapPoint(spawnPos, solidLayers))
                    continue;
            }

            GameObject copy = Instantiate(gameObject, spawnPos, transform.rotation);
            Rigidbody2D copyBody = copy.GetComponent<Rigidbody2D>();
            copyBody.bodyType = RigidbodyType2D.Dynamic;
            copyBody.simulated = true;
            copyBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            copyBody.linearDamping = Body.linearDamping;
            copyBody.gravityScale = Body.gravityScale;
            // Скорость задаём явно (без AddForce до SetVelocity — иначе
            // импульс перезапишет её на следующем fixed step'е в обратную
            // сторону при перекрытии коллайдеров).
            copyBody.linearVelocity = spread * speed;

            // Способность одноразовая: копия не должна умножаться повторно.
            CatAbility copyAbility = copy.GetComponent<CatAbility>();
            if (copyAbility != null)
                Destroy(copyAbility);

            // Копии не должны сталкиваться с оригиналом в первые кадры.
            if (selfCollider != null)
            {
                Collider2D copyCollider = copy.GetComponent<Collider2D>();
                if (copyCollider != null)
                    Physics2D.IgnoreCollision(selfCollider, copyCollider, true);
            }

            // Автоудаление копии даже после уничтожения оригинала (Destroy
            // переживает смерть самого GameObject кота).
            Destroy(copy, copyLifetime);
        }

        // Оригинал сохраняет скорость и направление: никаких изменений его
        // тела, кроме лёгкого импульса строго вдоль вектора полёта.
        if (forwardImpulse > 0f)
            Body.AddForce(direction * forwardImpulse, ForceMode2D.Impulse);
    }
}
