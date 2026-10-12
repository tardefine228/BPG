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

    protected override void Activate()
    {
        // Скорость читаем ДО любых Instantiate — она не должна измениться.
        Vector2 velocity = Body.linearVelocity;
        Vector2 direction = velocity.sqrMagnitude > 0.01f
            ? velocity.normalized : (Vector2)transform.right;
        float speed = velocity.magnitude;

        // ПОЧЕМУ КОТ РАНЬШЕ «ОСТАНАВЛИВАЛСЯ»: копии рождались прямо внутри
        // большого (масштаб x8) коллайдера оригинала и в этом же физическом
        // шаге сталкивались с ним — импульс разделения гасил скорость
        // исходного снаряда. Теперь копии создаются «призраками»: все их
        // коллайдеры (включая дочерние трассеров Ribbon/Ink) выключены,
        // поэтому столкнуть оригинала они физически не могут никогда.
        int copies = Mathf.Max(0, totalCats - 1);
        for (int i = 0; i < copies; i++)
        {
            // Углы распределяются симметрично вокруг направления полёта.
            float t = copies == 1 ? 0f : Mathf.Lerp(-1f, 1f, i / (float)(copies - 1));
            Vector2 spread = Quaternion.Euler(0f, 0f, fanAngle * t) * direction;
            Vector2 side = new Vector2(-spread.y, spread.x);

            // Полная копия кота-снаряда (визуал, спрайт, эффекты следа).
            GameObject copyGo = Instantiate(gameObject, transform.position, transform.rotation);
            copyGo.transform.SetParent(transform.parent, true);
            copyGo.name = gameObject.name;

            // Способность одноразовая: удаляем сразу, копия не умножается.
            CatAbility copyAbility = copyGo.GetComponent<CatAbility>();
            if (copyAbility != null)
            {
                copyAbility.enabled = false;
                Object.Destroy(copyAbility);
            }

            // «Призрачное» состояние копии: ни один коллайдер не включаем.
            foreach (Collider2D c in copyGo.GetComponentsInChildren<Collider2D>(true))
                c.enabled = false;

            Rigidbody2D copyBody = copyGo.GetComponent<Rigidbody2D>();
            copyBody.bodyType = RigidbodyType2D.Dynamic;
            copyBody.simulated = true;
            copyBody.interpolation = RigidbodyInterpolation2D.None;
            copyBody.linearDamping = Body.linearDamping;
            copyBody.gravityScale = Body.gravityScale;

            // Копия стартует чуть в стороне по перпендикуляру к своему лучу
            // веера — траектории сразу расходятся визуально.
            copyGo.transform.position += (Vector3)(side * 0.2f);
            copyBody.linearVelocity = spread * speed;
            if (forwardImpulse > 0f)
                copyBody.AddForce(spread * forwardImpulse, ForceMode2D.Impulse);

            // Спрайт копии — как у кота в полёте.
            copyGo.GetComponent<CatProjectile>()?.ShowLoaded();

            // Автоудаление копии даже после уничтожения оригинала
            // (Destroy переживает смерть самого GameObject кота).
            Object.Destroy(copyGo, copyLifetime);
        }

        // Прямая запись скорости вместо AddForce: гарантирует, что
        // оригинал сохраняет и направление, и величину полёта.
        Body.linearVelocity = direction * speed;
    }
}
