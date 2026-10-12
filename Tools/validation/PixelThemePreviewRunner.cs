// Copy only into the isolated preview project's Assets/Editor.
// Authoring + static visual checks: never enters Play Mode or simulates physics.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Object = UnityEngine.Object;

public static class PixelThemePreviewRunner
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        if (PlayerSettings.companyName != "BPGChecks") throw new InvalidOperationException("Use only the isolated preview project.");
        try
        {
            Directory.CreateDirectory("visual-previews");
            var callbacks = SnapshotCallbacks();
            if (!Environment.GetCommandLineArgs().Contains("-bpgThemeRenderOnly")) PixelThemeInstaller.ApplyAll();
            if (callbacks != SnapshotCallbacks()) throw new InvalidOperationException("UI callbacks or button availability changed.");
            VerifyPrefabs();
            RenderComparison();
            typeof(VisualPreviewRunner).GetMethod("RenderOnboardingVisuals", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            File.Copy("visual-previews/comic-to-tutorial.png", "visual-previews/pixel-tutorial.png", true);
            Capture(Camera.main, "pixel-settings");
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            foreach (var feedback in Object.FindObjectsByType<MenuControlFeedback>(FindObjectsInactive.Exclude))
            { Invoke(feedback, "Awake"); Invoke(feedback, "OnEnable"); }
            Capture(Camera.main, "pixel-main");
            File.WriteAllText("visual-previews/pixel-theme.done", "PASS: fonts, prefab wiring, original UI callbacks and static visual renders. No Play Mode.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            File.WriteAllText("visual-previews/pixel-theme.done", exception.ToString());
            EditorApplication.Exit(1);
        }
    }

    private static string SnapshotCallbacks()
    {
        var entries = new List<string>();
        foreach (string path in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            EditorSceneManager.OpenScene(path);
            foreach (Button button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include))
            {
                string item = path + ":" + Hierarchy(button.transform) + ":" + button.interactable;
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                {
                    Object target = button.onClick.GetPersistentTarget(i);
                    item += ":" + (target is Component component ? Hierarchy(component.transform) + "/" + target.GetType().Name : target?.name)
                        + "/" + button.onClick.GetPersistentMethodName(i);
                }
                entries.Add(item);
            }
        }
        return string.Join("\n", entries.OrderBy(s => s));
    }

    private static string Hierarchy(Transform t) => t.parent != null ? Hierarchy(t.parent) + "/" + t.name : t.name;
    private static void Invoke(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);

    private static void VerifyPrefabs()
    {
        int count = 0;
        foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Cats", "Assets/Prefabs/Mouses" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root.GetComponent<CatProjectile>() == null && root.GetComponent<MouseEnemy>() == null) continue;
            if (root.GetComponent<IdleSpriteAnimation>() == null) throw new InvalidOperationException("Idle animation missing: " + path);
            if (root.GetComponent<IdleSpriteAnimation>().Frames.Length != 0) throw new InvalidOperationException("Unexpected invented animation frames: " + path);
            count++;
        }
        if (count != 10) throw new InvalidOperationException("Expected five cat and five mouse prefabs; got " + count);
        File.WriteAllText("visual-previews/pixel-prefabs.txt", "10 idle animation components; empty frame lists; gameplay poses and colliders preserved.");
    }

    private static void Capture(Camera camera, string name)
        => typeof(VisualPreviewRunner).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { camera, name, null, 1920, 1080 });

    private static void RenderComparison()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var camera = new GameObject("PreviewCamera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(23, 22, 35, 255);
        camera.transform.position = new Vector3(0, 0, -10);
        camera.orthographic = true;
        var canvas = new GameObject("FontComparison", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var style = MenuButtonStyle.Default;
        Text(canvas.transform, style.font, "КНОПКИ И ШРИФТЫ", new Vector2(960, 984), new Vector2(1680, 70), 60, style.textColor);
        Text(canvas.transform, style.font, "Рыжая рамка фокуса · сжатие при нажатии · без дерева", new Vector2(960, 913), new Vector2(1680, 50), 32, style.textColor);
        string[] names = { "Tiny5", "PressStart2P", "Handjet" };
        float[] sizes = { 48, 30, 48 };
        for (int i = 0; i < names.Length; i++)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/PixelTheme/Fonts/" + names[i] + "-Pixel.asset");
            float y = 759 - i * 219;
            Text(canvas.transform, font, names[i] == "PressStart2P" ? "Press\nStart 2P" : names[i], new Vector2(197, y), new Vector2(320, 105), sizes[i], style.textColor);
            string[] labels = { "ИГРАТЬ", "ПРОДОЛЖИТЬ", "НАСТРОЙКИ" };
            Sprite[] sprites = { style.buttonSprite, style.secondaryButtonSprite, style.lightButtonSprite };
            for (int j = 0; j < 3; j++)
            {
                var plate = Plate(canvas.transform, sprites[j], new Vector2(610 + j * 475, y), new Vector2(430, 100));
                Text(plate.transform, font, labels[j], Vector2.zero, new Vector2(398, 80), sizes[i], j == 1 ? style.textColor : style.darkTextColor, true);
                if (j == 1) PixelFocusFrame.Create((RectTransform)plate.transform, style.highlightedColor, 3).SetActive(true);
            }
            Text(canvas.transform, font, "Ёжик, мышь, кот · 0123456789", new Vector2(1100, y - 73), new Vector2(1350, 38), sizes[i] * 0.6f, style.textColor);
        }
        var dark = Plate(canvas.transform, style.panelSprite, new Vector2(494, 124), new Vector2(770, 120));
        Text(dark.transform, style.font, "Плашки меню, счёта и паузы", Vector2.zero, new Vector2(700, 85), 38, style.textColor, true);
        var light = Plate(canvas.transform, style.illustrationPanelSprite, new Vector2(1369, 124), new Vector2(770, 120));
        Text(light.transform, style.font, "Рамки для текста и картинок", Vector2.zero, new Vector2(700, 85), 38, style.darkTextColor, true);
        Capture(camera, "pixel-font-comparison");
    }

    private static GameObject Plate(Transform parent, Sprite sprite, Vector2 position, Vector2 size)
    {
        var go = new GameObject("Plate", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.raycastTarget = false;
        return go;
    }

    private static void Text(Transform parent, TMP_FontAsset font, string content, Vector2 position, Vector2 size, float fontSize, Color color, bool local = false)
    {
        var text = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.transform.SetParent(parent, false);
        text.rectTransform.anchorMin = text.rectTransform.anchorMax = local ? new Vector2(0.5f, 0.5f) : Vector2.zero;
        text.rectTransform.anchoredPosition = position;
        text.rectTransform.sizeDelta = size;
        text.font = font;
        text.fontSharedMaterial = font.material;
        text.fontSize = fontSize;
        text.color = color;
        text.text = content;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
    }
}
