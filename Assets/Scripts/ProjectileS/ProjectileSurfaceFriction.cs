using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public sealed class ProjectileSurfaceFriction : MonoBehaviour
{
    [SerializeField, Min(0f)] private float groundDeceleration = 60f;
    [SerializeField, Min(0f)] private float rollingDeceleration = 720f;
    private Rigidbody2D body;
    private float groundedUntil = float.NegativeInfinity;

    private void Awake() => body = GetComponent<Rigidbody2D>();

    private void FixedUpdate()
    {
        if (!body.simulated || body.bodyType != RigidbodyType2D.Dynamic || Time.fixedTime > groundedUntil)
            return;
        // Дополнительное торможение останавливает качение круглого снаряда по земле.
        Vector2 velocity = body.linearVelocity;
        velocity.x = Mathf.MoveTowards(velocity.x, 0f, groundDeceleration * Time.fixedDeltaTime);
        body.linearVelocity = velocity;
        body.angularVelocity = Mathf.MoveTowards(body.angularVelocity, 0f,
            rollingDeceleration * Time.fixedDeltaTime);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
            if (collision.GetContact(i).normal.y > 0.5f)
            {
                groundedUntil = Time.fixedTime + Time.fixedDeltaTime * 1.5f;
                break;
            }
    }
}
