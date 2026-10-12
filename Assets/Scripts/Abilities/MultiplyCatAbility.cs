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

        // Оригинальный кот НЕ останавливается: он продолжает лететь по
        // своей траектории, а копии разлетаются от него симметричным веером.
        int copies = totalCats - 1;
        for (int i = 0; i < copies; i++)
        {
            // Углы распределяются симметрично вокруг направления полёта.
            float t = copies == 1 ? 0f : Mathf.Lerp(-1f, 1f, i / (float)(copies - 1));
            float angleDeg = fanAngle * t;
            Vector2 spread = Quaternion.Euler(0f, 0f, angleDeg) * direction;

            // Полная копия кота-снаряда (визуал, коллайдер, эффекты).
            GameObject copyGo = Instantiate(gameObject, transform.position, transform.rotation);
            copyGo.transform.SetParent(transform.parent, true);
            copyGo.name = name;

            // Способность одноразовая: копия не должна умножаться повторно.
            Destroy(copyGo.GetComponent<CatAbility>());

            Rigidbody2D copyBody = copyGo.GetComponent<Rigidbody2D>();
            copyBody.bodyType = RigidbodyType2D.Dynamic;
            copyBody.simulated = true;
            copyBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            // Смещаем копию чуть в сторону, чтобы не столкнуться с
            // оригиналом в момент создания (веер стартует раздельно).
            copyGo.transform.position += (Vector3)(new Vector2(-spread.y, spread.x) * 0.25f);
            copyBody.linearVelocity = spread * speed;
            copyBody.AddForce(spread * forwardImpulse, ForceMode2D.Impulse);

            // Спрайт копии — как у кота в полёте.
            copyGo.GetComponent<CatProjectile>()?.ShowLoaded();

            // Автоудаление копии даже после уничтожения оригинала (Destroy
            // переживает смерть самого GameObject кота).
            Destroy(copyGo, copyLifetime);
        }
    }
}
