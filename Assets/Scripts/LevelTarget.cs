using UnityEngine;

/// <summary>
/// Marks a destructible object that must be knocked down to clear a level.
/// </summary>
public sealed class LevelTarget : MonoBehaviour
{
    [SerializeField] private float fallY = -6f;
    [SerializeField] private float impactSpeed = 2f;
    private const float IgnoreCollisionsFor = 0.8f;

    public bool IsCleared { get; private set; }

    private void Update()
    {
        if (IsCleared)
        {
            return;
        }

        if (transform.position.y <= fallY)
        {
            IsCleared = true;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (IsCleared || collision == null || collision.collider == null)
        {
            return;
        }

        if (Time.timeSinceLevelLoad < IgnoreCollisionsFor)
        {
            return;
        }

        if (collision.collider.GetComponent<LevelTarget>() != null)
        {
            return;
        }

        if (collision.rigidbody != null && collision.rigidbody.bodyType == RigidbodyType2D.Static)
        {
            return;
        }

        if (collision.relativeVelocity.sqrMagnitude >= impactSpeed * impactSpeed)
        {
            IsCleared = true;
        }
    }
}
