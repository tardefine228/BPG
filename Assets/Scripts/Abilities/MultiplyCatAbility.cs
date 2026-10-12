using UnityEngine;

// Умножение: по ЛКМ в полёте кот разлетается на несколько копий.
// Оригинал продолжает лететь с прежней скоростью; копии разлетаются
// симметричным веером, каждая летит как обычный снаряд.
public sealed class MultiplyCatAbility : CatAbility
{
    [SerializeField, Min(1)] private int totalCats = 3;
    [SerializeField, Range(0f, 90f)] private float fanAngle = 20f;
    [SerializeField, Min(0f)] private float forwardImpulse = 4f;
    [SerializeField, Min(0.1f)] private float copyLifetime = 2.5f;
    [SerializeField] private LayerMask solidLayers = ~0;

    protected override void Activate()
    {
        // Скорость читаем ДО любых Instantiate — она не должна измениться.
        Vector2 velocity = Body.linearVelocity;
        Vector2 direction = velocity.sqrMagnitude > 0.01f
            ? velocity.normalized : (Vector2)transform.right;
        float speed = Mathf.Max(velocity.magnitude, 1f);

        // Радиус «зоны вылета»: чтобы копия не рождалась внутри коллайдера
        // оригинала (иначе физика столкнёт их и погасит скорость).
        Collider2D selfCollider = GetComponent<Collider2D>();
        float spawnRadius = selfCollider != null
            ? selfCollider.bounds.extents.magnitude + 0.1f
            : 0.5f;

        // Веер строим в мировых координатах: спрайт кота повёрнут на 90°,
        // поэтому направление разлёта берём строго от фактической скорости,
        // а не от локальных осей transform.
        for (int i = 0; i < totalCats - 1; i++)
        {
            int side = (i % 2 == 0) ? 1 : -1;
            float angleDeg = fanAngle * (0.5f + i / 2) * side;
            Vector2 spread = Quaternion.Euler(0f, 0f, angleDeg) * direction;

            // Смещение спавна вдоль вектора «разлёта», а не вдоль полёта:
            // так копия гарантированно рождается вне коллайдера оригинала
            // (никакого перекрытия -> физика не толкает их друг в друга
            // и не гасит скорость оригинала).
            Vector2 spawnPos = (Vector2)transform.position + spread * spawnRadius;

            // Не спавним копию внутри стены/блока — иначе она застрянет.
            if (Physics2D.OverlapPoint(spawnPos, solidLayers))
                continue;

            GameObject copy = Instantiate(gameObject, spawnPos, transform.rotation);

            Rigidbody2D copyBody = copy.GetComponent<Rigidbody2D>();
            copyBody.bodyType = RigidbodyType2D.Dynamic;
            copyBody.simulated = true;
            copyBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            copyBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            copyBody.linearDamping = Body.linearDamping;
            copyBody.gravityScale = Body.gravityScale;
            // Скорость задаём явно: копии летят как настоящие снаряды
            // и нормально сталкиваются со всем окружением.
            copyBody.linearVelocity = spread * speed;

            // Спрайт копии — как у кота в полёте.
            copyGo.GetComponent<CatProjectile>()?.ShowLoaded();

            // Копии стартуют сразу в позе полёта (клон мог родиться с
            // «очередным» спрайтом).
            copy.GetComponent<CatProjectile>()?.ShowLoaded();

            // Полётные эффекты у клона включаем вручную: BeginFlight вызывается
            // только для оригинала в SlingshotController. Без этого клон после
            // первого касания уходил бы в «декоративный вылет» без физики.
            copy.GetComponent<ProjectileComicEffects>()?.BeginFlight();
            copy.GetComponent<CatFlightEffects>()?.BeginFlight(
                Camera.main != null ? Camera.main.transform.position.y - 15f : -15f);

            // Снаряды летают быстро и мелкие — включаем Continuous Dynamic,
            // иначе на большой скорости клон может «протуннелить» сквозь
            // тонкие блоки/врагов (в Unity это основная причина того, что
            // клоны «ни обо что не ударяются»).
            copyBody.BulletProof();

            // Копии игнорируют только оригинал (чтобы не столкнуть его),
            // но сталкиваются и с другими копиями, и со всем миром.
            Collider2D copyCollider = copy.GetComponent<Collider2D>();
            if (selfCollider != null && copyCollider != null)
                Physics2D.IgnoreCollision(selfCollider, copyCollider, true);

            // Автоудаление копии даже после уничтожения оригинала (Destroy
            // переживает смерть самого GameObject кота).
            Destroy(copy, copyLifetime);
        }

        // Лёгкий импульс оригиналу — строго вдоль вектора полёта, чтобы
        // сохранить баллистику и добавить «рывок» при разделении.
        if (forwardImpulse > 0f)
            Body.AddForce(direction * forwardImpulse, ForceMode2D.Impulse);
    }
}
