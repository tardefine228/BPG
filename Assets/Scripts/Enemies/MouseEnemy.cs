using System;
using System.Collections;
using UnityEngine;

// Мышь получает урон от сильных столкновений; при 0 HP считается уничтоженной.
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

    [Header("Падение и смерть")]
    [Tooltip("Мышь погибает, если падает ниже этой высоты.")]
    [SerializeField] private float fallY = -6f;
    [Tooltip("Задержка перед скрытием мыши; флаг IsDead ставится сразу.")]
    [SerializeField, Min(0f)] private float deathDelay = 0.4f;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }

    // LevelSession начисляет очки за фактический урон и за смерть.
    public event Action<MouseEnemy, float> Damaged;
    public event Action<MouseEnemy> Died;

    private float nextHitTime;

    private void Awake()
    {
        Health = Mathf.Max(1f, maxHealth);
    }

    private void Update()
    {
        if (!IsDead && transform.position.y <= fallY)
            Kill();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsDead || collision == null || Time.time < nextHitTime)
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
