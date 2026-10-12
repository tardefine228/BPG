using System.Collections.Generic;
using UnityEngine;

public static class BlastUtility
{
    public static void Explode(Vector2 center, float radius, float enemyDamage,
        float blockHitStrength, float explosionImpulse, LayerMask layers, Rigidbody2D source = null)
    {
        if (radius <= 0f)
            return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(center, radius, layers);
        var damagedMice = new HashSet<MouseEnemy>();
        var hitTargets = new HashSet<LevelTarget>();
        var pushedBodies = new HashSet<Rigidbody2D>();

        foreach (Collider2D hit in hits)
        {
            if (hit == null || hit.isTrigger)
                continue;

            Rigidbody2D body = hit.attachedRigidbody;
            if (body == source)
                continue;

            Vector2 closest = hit.ClosestPoint(center);
            Vector2 offset = closest - center;
            float distance = offset.magnitude;
            float falloff = Mathf.Clamp01(1f - distance / radius);
            if (falloff <= 0f)
                continue;

            Vector2 direction = offset.sqrMagnitude > 0.001f
                ? offset.normalized : Vector2.up;

            MouseEnemy enemy = hit.GetComponentInParent<MouseEnemy>();
            if (enemy != null && damagedMice.Add(enemy))
                enemy.TakeDamage(enemyDamage * falloff);

            LevelTarget target = hit.GetComponentInParent<LevelTarget>();
            if (target != null && hitTargets.Add(target))
                target.ApplyAbilityHit(blockHitStrength, direction);

            if (body != null && body.bodyType == RigidbodyType2D.Dynamic && body.simulated
                && pushedBodies.Add(body))
                body.AddForce(direction * (explosionImpulse * falloff), ForceMode2D.Impulse);
        }
    }
}
