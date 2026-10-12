using UnityEngine;

// Кот резко пикирует вниз для удара по постройке сверху.
public sealed class DiveCatAbility : CatAbility
{
    [SerializeField, Min(0f)] private float diveImpulse = 22f;
    [SerializeField] private bool cancelUpwardVelocity = true;

    protected override void Activate()
    {
        if (cancelUpwardVelocity && Body.linearVelocity.y > 0f)
            Body.linearVelocity = new Vector2(Body.linearVelocity.x, 0f);

        Body.AddForce(Vector2.down * diveImpulse, ForceMode2D.Impulse);
        GetComponent<ProjectileComicEffects>()?.PlayAbilityEffect(ComicParticleBurst.AbilityEffect.Speed);
    }
}
