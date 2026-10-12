using UnityEngine;

// Взрывная волна вокруг кота. Наносит урон мышам, повреждает блоки и отбрасывает тела.
public sealed class BlastCatAbility : CatAbility
{
    [SerializeField, Min(0.1f)] private float radius = 4f;
    [SerializeField, Min(0f)] private float enemyDamage = 60f;
    [SerializeField, Min(0f)] private float blockHitStrength = 10f;
    [SerializeField, Min(0f)] private float explosionImpulse = 14f;
    [SerializeField] private LayerMask affectedLayers = ~0;

    protected override void Activate()
    {
        BlastUtility.Explode(Body.position, radius, enemyDamage,
            blockHitStrength, explosionImpulse, affectedLayers, Body);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
