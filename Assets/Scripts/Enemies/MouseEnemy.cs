
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public sealed class MouseEnemy : MonoBehaviour
{
    [Header("Здоровье")]
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    [Header("Урон от столкновений")]
    [SerializeField, Min(0f)] private float minImpactSpeed = 2f;
    [SerializeField, Min(0f)] private float damageMultiplier = 2f;
    [SerializeField, Min(0f)] private float hitCooldown = 0.15f;

    [Header("Смерть")]
    [SerializeField, Min(0f)] private float deathDelay = 0.4f;

    private float health;
    private float nextHitTime;
    private bool isDead;

    public bool IsDead => isDead;
    public float Health => health;

    private void Awake()
    {
        health = maxHealth;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isDead || Time.time < nextHitTime)
            return;

        float speed = collision.relativeVelocity.magnitude;

        if (speed < minImpactSpeed)
            return;

        float damage = (speed - minImpactSpeed)
            * damageMultiplier;

        TakeDamage(damage);
        nextHitTime = Time.time + hitCooldown;
    }

    public void TakeDamage(float damage)
    {
        if (isDead || damage <= 0f)
            return;

        health = Mathf.Max(0f, health - damage);

        if (health <= 0f)
        {
            isDead = true;
            StartCoroutine(Die());
        }
    }

    private IEnumerator Die()
    {
        // Здесь позже добавим анимацию смерти.
        yield return new WaitForSeconds(deathDelay);

        // Объект остаётся в сцене до завершения уровня,
        // чтобы LevelSession мог проверить IsDead.
        foreach (Collider2D hitbox in
            GetComponentsInChildren<Collider2D>())
        {
            hitbox.enabled = false;
        }

        foreach (SpriteRenderer sprite in
            GetComponentsInChildren<SpriteRenderer>())
        {
            sprite.enabled = false;
        }

        Rigidbody2D body = GetComponent<Rigidbody2D>();
        body.simulated = false;
    }
}
