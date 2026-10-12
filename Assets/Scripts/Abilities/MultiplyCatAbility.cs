using UnityEngine;
using System.Collections.Generic;
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
        GetComponent<ProjectileComicEffects>()?.PlayAbilityEffect(ComicParticleBurst.AbilityEffect.Split);
        Vector2 velocity = Body.linearVelocity;
        Vector2 direction = velocity.sqrMagnitude > 0.01f
            ? velocity.normalized
            : (Vector2)transform.right;

        float speed = Mathf.Max(velocity.magnitude, 1f);

        Collider2D selfCollider = GetComponent<Collider2D>();

        float spawnRadius = selfCollider != null
            ? selfCollider.bounds.extents.magnitude + 0.1f
            : 0.5f;

        // Создаём ровно 3 копии независимо от totalCats.
        const int copiesCount = 3;
        List<Collider2D> spawnedColliders = new List<Collider2D>();

        for (int i = 0; i < copiesCount; i++)
        {
            float angleDeg = fanAngle * (i - 1);
            Vector2 spread =
                Quaternion.Euler(0f, 0f, angleDeg) * direction;

            Vector2 spawnPos =
                (Vector2)transform.position + spread * spawnRadius;

            // Если точка занята блоком, пробуем сместить копию дальше.
            if (Physics2D.OverlapPoint(spawnPos, solidLayers))
                spawnPos += spread * spawnRadius;

            GameObject copy = Instantiate(
                gameObject,
                spawnPos,
                transform.rotation
            );

            // Удаляем способность у копии, чтобы она не активировалась повторно.
            CatAbility ability = copy.GetComponent<CatAbility>();
            if (ability != null)
                Destroy(ability);

            Rigidbody2D copyBody = copy.GetComponent<Rigidbody2D>();
            copyBody.bodyType = RigidbodyType2D.Dynamic;
            copyBody.simulated = true;
            copyBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            copyBody.collisionDetectionMode =
                CollisionDetectionMode2D.Continuous;
            copyBody.linearDamping = Body.linearDamping;
            copyBody.gravityScale = Body.gravityScale;
            copyBody.linearVelocity = spread * speed;

            copy.GetComponent<CatProjectile>()?.ShowLoaded();

            copy.GetComponent<ProjectileComicEffects>()?.BeginFlight();
            copy.GetComponent<ProjectileComicEffects>()?.HighlightSplitTrail();

            copy.GetComponent<CatFlightEffects>()?.BeginFlight(
                Camera.main != null
                    ? Camera.main.transform.position.y - 15f
                    : -15f
            );

            Collider2D copyCollider = copy.GetComponent<Collider2D>();

            if (copyCollider != null)
            {
                // Не сталкиваем копию с оригиналом.
                if (selfCollider != null)
                    Physics2D.IgnoreCollision(selfCollider, copyCollider, true);

                // Не сталкиваем копию с ранее созданными копиями.
                foreach (Collider2D other in spawnedColliders)
                {
                    if (other != null)
                        Physics2D.IgnoreCollision(copyCollider, other, true);
                }

                spawnedColliders.Add(copyCollider);
            }

            if (selfCollider != null && copyCollider != null)
                Physics2D.IgnoreCollision(selfCollider, copyCollider, true);

            Destroy(copy, copyLifetime);
        }

        // Удаляем оригинального кота после создания трёх копий.
        Destroy(gameObject);
    }

}
