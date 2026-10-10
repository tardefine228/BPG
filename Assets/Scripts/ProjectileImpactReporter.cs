using System;
using UnityEngine;

public class ProjectileImpactReporter : MonoBehaviour
{
    private Action<Vector2> onImpact;
    private bool armed;

    public void Arm(Action<Vector2> callback)
    {
        onImpact = callback;
        armed = true;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!armed)
            return;
        armed = false;
        Vector2 point = collision.contactCount > 0
            ? collision.GetContact(0).point : (Vector2)transform.position;
        onImpact?.Invoke(point);
        onImpact = null;
    }
}
