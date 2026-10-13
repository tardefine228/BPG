using UnityEngine;

[CreateAssetMenu(menuName = "UI/Startup Splash Settings")]
public sealed class StartupSplashSettings : ScriptableObject
{
    [Header("Заставка компании при запуске из Boot")]
    public Sprite logo;
    public string companyName = "BP GAMES";
    public Color backgroundColor = new Color32(9, 9, 12, 255);
    public Color textColor = new Color32(244, 239, 225, 255);

    [Header("Кот под названием компании")]
    public bool showRunningCat = true;
    public Sprite[] runningCatFrames = new Sprite[0];
    [Range(1f, 30f)] public float runningCatFramesPerSecond = 16f;
    [Tooltip("Размер одного пикселя исходного кота в единицах Canvas.")]
    [Range(1f, 12f)] public float runningCatPixelScale = 6f;
    [Tooltip("Промежуток между нижней границей надписи и котом.")]
    [Min(0f)] public float runningCatTextGap = 40f;
    [Min(0f)] public float runningCatDelay = 0.15f;
    [Min(0.1f)] public float runningCatDuration = 3.6f;

    [Header("Реальные секунды")]
    [Min(0f)] public float fadeInDuration = 1.2f;
    [Min(0f)] public float holdDuration = 3.9f;
    [Min(0f)] public float fadeOutDuration = 0.6f;
    [Min(0f)] public float menuRevealDuration = 0.6f;
}
