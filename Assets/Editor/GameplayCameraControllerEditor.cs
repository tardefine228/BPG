using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GameplayCameraController))]
public sealed class GameplayCameraControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("До Play камера показывает конечный игровой кадр. При запуске Intro Zoom ненадолго отдаляет его.", MessageType.Info);
        if (GUILayout.Button("Показать карту в окне Scene"))
        {
            var controller = (GameplayCameraController)target;
            var camera = controller.GetComponent<Camera>();
            SceneView scene = SceneView.lastActiveSceneView;
            if (scene == null)
                scene = EditorWindow.GetWindow<SceneView>();
            scene.in2DMode = true;
            scene.LookAt(controller.transform.position + Vector3.forward * 10f,
                Quaternion.identity, camera.orthographicSize, true, true);
            scene.Repaint();
        }
    }
}
