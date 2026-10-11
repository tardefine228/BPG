using UnityEngine;

[CreateAssetMenu(menuName = "UI/Scene Transition Settings")]
public sealed class SceneTransitionSettings : ScriptableObject
{
    [Header("Экран перехода между сценами")]
    public string loadingCaption = "ЗАГРУЗКА";
    [Header("Время (реальные секунды)")]
    [Min(0f)] public float minimumLoadingTime = 0.65f;
    [Min(0f)] public float fadeDuration = 0.3f;
    [Header("Оформление")]
    public Color backgroundColor = new Color32(17, 24, 36, 255);
    public Color textColor = new Color32(244, 239, 225, 255);
    public Color accentColor = new Color32(230, 155, 82, 255);
}
