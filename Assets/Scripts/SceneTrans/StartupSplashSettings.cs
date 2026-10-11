using UnityEngine;

[CreateAssetMenu(menuName = "UI/Startup Splash Settings")]
public sealed class StartupSplashSettings : ScriptableObject
{
    [Header("Заставка компании при запуске из Boot")]
    public Sprite logo;
    public string companyName = "BP GAMES";
    public Color backgroundColor = new Color32(9, 9, 12, 255);
    public Color textColor = new Color32(244, 239, 225, 255);

    [Header("Реальные секунды")]
    [Min(0f)] public float fadeInDuration = 1.2f;
    [Min(0f)] public float holdDuration = 3f;
    [Min(0f)] public float fadeOutDuration = 0.6f;
    [Min(0f)] public float menuRevealDuration = 0.6f;
}
