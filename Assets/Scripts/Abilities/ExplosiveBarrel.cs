using System.Collections;
using UnityEngine;

// Новый вариант объекта: бочка взрывается, когда её LevelTarget считается уничтоженным.
[RequireComponent(typeof(LevelTarget))]
public sealed class ExplosiveBarrel : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float radius = 4.5f;
    [SerializeField, Min(0f)] private float enemyDamage = 75f;
    [SerializeField, Min(0f)] private float blockHitStrength = 10f;
    [SerializeField, Min(0f)] private float explosionImpulse = 16f;
    [SerializeField, Min(0f)] private float hideDelay = 0.15f;
    [SerializeField] private LayerMask affectedLayers = ~0;

    private LevelTarget target;
    private bool exploded;

    private void Awake()
    {
        target = GetComponent<LevelTarget>();
    }

    private void OnEnable()
    {
        if (target == null)
            target = GetComponent<LevelTarget>();
        target.Cleared += OnCleared;
    }

    private void OnDisable()
    {
        if (target != null)
            target.Cleared -= OnCleared;
    }

    private void OnCleared(LevelTarget cleared)
    {
        if (exploded)
            return;
        exploded = true;
        Rigidbody2D ownBody = GetComponent<Rigidbody2D>();
        BlastUtility.Explode(transform.position, radius, enemyDamage, blockHitStrength,
            explosionImpulse, affectedLayers, ownBody);
        StartCoroutine(HideBarrel());
    }

    private IEnumerator HideBarrel()
    {
        if (hideDelay > 0f)
            yield return new WaitForSeconds(hideDelay);
        foreach (SpriteRenderer sprite in GetComponentsInChildren<SpriteRenderer>())
            sprite.enabled = false;
        foreach (Collider2D hitbox in GetComponentsInChildren<Collider2D>())
            hitbox.enabled = false;
        Rigidbody2D body = GetComponent<Rigidbody2D>();
        if (body != null)
            body.simulated = false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
