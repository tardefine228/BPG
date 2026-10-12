using System;
using System.Collections;
using UnityEngine;

// Мышь погибает от сильных столкновений с шаром, а также при падении с высоты
// (или при провале за пределы уровня); при 0 HP считается уничтоженной.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public sealed class MouseEnemy : MonoBehaviour
{
    [Header("Роль на уровне")]
    [Tooltip("Если включено, эту мышь обязательно убить для победы.")]
    [SerializeField] private bool countsForVictory = true;
    public bool CountsForVictory => countsForVictory;

    [Header("Здоровье")]
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    [Header("Урон от столкновений")]
    [Tooltip("Удары медленнее этого порога не наносят урон и не дают очки.")]
    [SerializeField, Min(0f)] private float minImpactSpeed = 2f;
    [Tooltip("Чем выше скорость столкновения, тем больше урон.")]
    [SerializeField, Min(0f)] private float damageMultiplier = 2f;
    [Tooltip("Защита от многократного урона за одно столкновение.")]
    [SerializeField, Min(0f)] private float hitCooldown = 0.15f;

    [Header("Смерть")]
    [Tooltip("Мышь погибает, если проваливается ниже этой высоты (за пределы уровня).")]
    [SerializeField] private float fallY = -6f;
    [Tooltip("Задержка перед скрытием мыши; флаг IsDead ставится сразу.")]
    [SerializeField, Min(0f)] private float deathDelay = 0.4f;

    [Header("Смерть от падения с высоты")]
    [Tooltip("Если включено, мышь погибает (или получает урон), когда падает с высоты " +
             "больше указанного порога: при приземлении либо сразу в полёте.")]
    [SerializeField] private bool dieOnFallDamage = true;
    [Tooltip("Минимальная высота падения (в блоках/юнитах), при которой приземление убивает " +
             "(или ранит — см. «Мгновенная смерть при падении»).")]
    [SerializeField, Min(0f)] private float fallDamageHeight = 2f;
    [Tooltip("Мгновенно убивать мышь при таком падении. Если выключено — наносится урон, " +
             "зависящий от высоты падения (см. «Множитель урона»).")]
    [SerializeField] private bool instakillOnFall = true;
    [Tooltip("Урон за каждый блок сверх порога высоты (используется, если мгновенная смерть выключена).")]
    [SerializeField, Min(0f)] private float fallDamagePerUnit = 50f;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }

    // LevelSession начисляет очки за фактический урон и за смерть.
    public event Action<MouseEnemy, float> Damaged;
    public event Action<MouseEnemy> Died;

    private float nextHitTime;

    // Высота, с которой мышь начала свободное падение (0 — не падает).
    private float fallStartY;

    private void Awake()
    {
        Health = Mathf.Max(1f, maxHealth);
    }

    private void Update()
    {
        if (IsDead)
            return;

        // Глубокий провал за пределы уровня — смерть как и раньше.
        if (transform.position.y <= fallY)
        {
            Kill();
            return;
        }

        TrackFall();
    }

    private void TrackFall()
    {
        if (!dieOnFallDamage)
            return;

        Rigidbody2D body = GetComponent<Rigidbody2D>();
        if (body == null)
            return;

        bool isFalling = !body.isKinematic
                         && body.gravityScale > 0f
                         && body.linearVelocity.y < -0.1f
                         && Mathf.Abs(body.linearVelocity.x) < 0.5f
                         && !IsGrounded();

        if (isFalling)
        {
            // Запоминаем высоту начала падения (обновляем, пока падение продолжается).
            fallStartY = fallStartY > 0f
                ? Mathf.Max(fallStartY, transform.position.y)
                : transform.position.y;

            float drop = fallStartY - transform.position.y;

            // Если мышь падает вниз без шанса приземлиться (за пределы уровня) —
            // погибает сразу, не дожидаясь касания дна.
            if (instakillOnFall && drop >= fallDamageHeight && WouldLeaveLevel(body))
            {
                Kill();
                return;
            }
        }
        else if (fallStartY > 0f)
        {
            // Падение прекратилось (приземлилась или остановилась) — считаем перепад высот.
            float drop = fallStartY - transform.position.y;
            fallStartY = 0f;

            if (drop >= fallDamageHeight)
                ApplyFallDamage(drop);
        }
    }

    // Должна ли мышь по текущей траектории провалиться ниже kill-зоны уровня.
    private bool WouldLeaveLevel(Rigidbody2D body)
    {
        float predictedY = transform.position.y + body.linearVelocity.y * 0.3f;
        return predictedY <= fallY || transform.position.y <= fallY + 0.5f;
    }

    private void ApplyFallDamage(float dropHeight)
    {
        if (instakillOnFall)
        {
            Kill();
            return;
        }

        float damage = (dropHeight - fallDamageHeight) * fallDamagePerUnit;
        if (damage <= 0f)
            damage = fallDamagePerUnit; // падение ровно на порог тоже должно ранить

        TakeDamage(damage);
    }

    // Проверяем, стоит ли мышь на чём-нибудь (чтобы не считать падением скольжение по земле).
    private bool IsGrounded()
    {
        Collider2D self = GetComponent<Collider2D>();
        if (self == null)
            return false;

        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        var results = UnityEngine.Pool.ListPool<Collider2D>.Get();
        bool grounded = Physics2D.OverlapCollider(self, filter, results) != 0
                        && HasContactBelow(results);
        UnityEngine.Pool.ListPool<Collider2D>.Release(results);
        return grounded;
    }

    private bool HasContactBelow(System.Collections.Generic.IList<Collider2D> overlaps)
    {
        float selfY = transform.position.y;
        for (int i = 0; i < overlaps.Count; i++)
        {
            Collider2D other = overlaps[i];
            if (other == null || other.isTrigger)
                continue;

            Bounds b = other.bounds;
            // Контакт снизу: верх другого коллайдера не выше центра мыши.
            if (b.max.y <= selfY + 0.1f)
                return true;
        }

        return false;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsDead || collision == null)
            return;

        // Столкновение с землёй/объектом во время падения засчитываем как приземление.
        if (dieOnFallDamage && fallStartY > 0f)
        {
            float drop = fallStartY - transform.position.y;
            fallStartY = 0f;

            if (drop >= fallDamageHeight)
            {
                ApplyFallDamage(drop);
                return;
            }
        }

        if (Time.time < nextHitTime)
            return;

        float speed = collision.relativeVelocity.magnitude;
        if (speed <= minImpactSpeed)
            return;

        float damage = (speed - minImpactSpeed) * damageMultiplier;
        if (damage <= 0f)
            return;

        nextHitTime = Time.time + hitCooldown;
        TakeDamage(damage);
    }

    public void TakeDamage(float damage)
    {
        if (IsDead || damage <= 0f)
            return;

        float actualDamage = Mathf.Min(Health, damage);
        Health -= actualDamage;
        Damaged?.Invoke(this, actualDamage);

        if (Health <= 0f)
            Kill();
    }

    private void Kill()
    {
        if (IsDead)
            return;

        IsDead = true;
        Health = 0f;
        Died?.Invoke(this);
        StartCoroutine(DeathRoutine());
    }

    private IEnumerator DeathRoutine()
    {
        if (deathDelay > 0f)
            yield return new WaitForSeconds(deathDelay);

        // Не уничтожаем GameObject: LevelSession хранит на него ссылку.
        foreach (Collider2D hitbox in GetComponentsInChildren<Collider2D>())
            hitbox.enabled = false;

        foreach (SpriteRenderer sprite in GetComponentsInChildren<SpriteRenderer>())
            sprite.enabled = false;

        Rigidbody2D body = GetComponent<Rigidbody2D>();
        if (body != null)
            body.simulated = false;
    }
}
