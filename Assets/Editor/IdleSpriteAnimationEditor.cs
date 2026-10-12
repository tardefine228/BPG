using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Preview changes only SpriteRenderer.sprite in Prefab/Edit Mode and restores it on exit.
[CustomEditor(typeof(IdleSpriteAnimation)), CanEditMultipleObjects]
public sealed class IdleSpriteAnimationEditor : Editor
{
    private Texture2D sheet;
    private bool previewing;
    private double previewStarted;
    private SpriteRenderer previewRenderer;
    private Sprite originalSprite;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Коты проигрывают кадры только в очереди. Мыши — пока живы и стоят на месте. Пустой список ничего не меняет.", MessageType.Info);
        sheet = (Texture2D)EditorGUILayout.ObjectField("Лист кадров", sheet, typeof(Texture2D), false);
        using (new EditorGUI.DisabledScope(sheet == null))
        {
            if (GUILayout.Button("Заполнить кадрами из листа"))
            {
                var sprites = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(sheet))
                    .OfType<Sprite>().OrderBy(s => Regex.Replace(s.name, @"\d+", m => m.Value.PadLeft(10, '0'))).ToArray();
                if (sprites.Length == 0)
                    EditorGUILayout.HelpBox("Импортируйте лист как Sprite / Multiple и разрежьте его в Sprite Editor.", MessageType.Warning);
                else
                {
                    Undo.RecordObjects(targets, "Назначить кадры ожидания");
                    serializedObject.Update();
                    var frames = serializedObject.FindProperty("frames");
                    frames.arraySize = sprites.Length;
                    for (int i = 0; i < sprites.Length; i++) frames.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
                    serializedObject.ApplyModifiedProperties();
                }
            }
        }
        var animation = (IdleSpriteAnimation)target;
        using (new EditorGUI.DisabledScope(Application.isPlaying || targets.Length != 1 || animation.Frames.Length == 0
            || (AnimationMode.InAnimationMode() && !previewing)))
            if (GUILayout.Button(previewing ? "Остановить предпросмотр" : "Предпросмотр без запуска игры"))
            {
                if (previewing) StopPreview();
                else
                {
                    previewRenderer = animation.GetComponent<SpriteRenderer>();
                    originalSprite = previewRenderer.sprite;
                    AnimationMode.StartAnimationMode();
                    AnimationMode.AddPropertyModification(
                        EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"),
                        new PropertyModification { target = previewRenderer, propertyPath = "m_Sprite", objectReference = originalSprite }, true);
                    previewStarted = EditorApplication.timeSinceStartup;
                    previewing = true;
                    EditorApplication.update += UpdatePreview;
                }
            }
    }

    private void UpdatePreview()
    {
        if (target == null || previewRenderer == null || Application.isPlaying)
        {
            StopPreview();
            return;
        }
        var animation = (IdleSpriteAnimation)target;
        int count = animation.Frames.Length;
        if (count == 0) { StopPreview(); return; }
        int cycle = animation.PingPong && count > 1 ? count * 2 - 2 : count;
        int step = (int)((EditorApplication.timeSinceStartup - previewStarted) * animation.FramesPerSecond) % cycle;
        int index = step >= count ? cycle - step : step;
        if (animation.Frames[index] != null) previewRenderer.sprite = animation.Frames[index];
        SceneView.RepaintAll();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    private void StopPreview()
    {
        EditorApplication.update -= UpdatePreview;
        if (previewing) AnimationMode.StopAnimationMode();
        if (previewing && previewRenderer != null) previewRenderer.sprite = originalSprite;
        previewing = false;
    }

    private void OnDisable() => StopPreview();
}
