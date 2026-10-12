using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit authoring command only; never runs automatically when opening the project.
public static class PixelThemeInstaller
{
    private const string Theme = "Assets/UI/PixelTheme/";
    private static readonly string[] FontNames = { "Tiny5", "PressStart2P", "Handjet" };

    [MenuItem("BPG/Оформление/Обновить шрифт из MenuButtonStyle")]
    public static void ApplyChosenFont()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var style = AssetDatabase.LoadAssetAtPath<MenuButtonStyle>("Assets/Resources/MenuButtonStyle.asset");
        if (style == null || style.font == null) throw new InvalidOperationException("Назначьте Font у MenuButtonStyle.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (string path in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var scene = EditorSceneManager.OpenScene(path);
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) style.ApplyFont(text);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/UI", "Assets/Prefabs" })
                .Select(AssetDatabase.GUIDToAssetPath).Distinct())
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var texts = root.GetComponentsInChildren<TMP_Text>(true);
                    if (texts.Length == 0) continue;
                    foreach (var text in texts) style.ApplyFont(text);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
    }

    [MenuItem("BPG/Оформление/Применить пиксельный стиль ко всем интерфейсам")]
    public static void ApplyAll()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            ConfigureSprites();
            var fonts = FontNames.Select(CreateFont).ToArray();
            var style = AssetDatabase.LoadAssetAtPath<MenuButtonStyle>("Assets/Resources/MenuButtonStyle.asset");
            if (style == null) throw new InvalidOperationException("MenuButtonStyle asset is missing.");
            SetStyle(style, fonts[0]);
            for (int i = 0; i < fonts.Length; i++)
            {
                string path = Theme + "Style-" + FontNames[i] + ".asset";
                var variant = AssetDatabase.LoadAssetAtPath<MenuButtonStyle>(path);
                if (variant == null)
                {
                    variant = ScriptableObject.CreateInstance<MenuButtonStyle>();
                    AssetDatabase.CreateAsset(variant, path);
                }
                SetStyle(variant, fonts[i]);
            }
            AssetDatabase.SaveAssets();
            InstallAnimationPrefabs();
            foreach (string path in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" })
                .Select(AssetDatabase.GUIDToAssetPath))
            {
                var scene = EditorSceneManager.OpenScene(path);
                foreach (GameObject root in scene.GetRootGameObjects()) StyleHierarchy(root, style);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("PIXEL THEME: " + path);
            }
            foreach (string path in new[] { "Assets/Prefabs/SceneTransition.prefab", "Assets/Prefabs/SceneLoadingPanel.prefab", "Assets/Prefabs/StartupSplash.prefab" })
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try { StyleHierarchy(root, style); PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            CreateTemplates(style);
            AssetDatabase.SaveAssets();
            Debug.Log("PIXEL THEME COMPLETE: UI callbacks preserved; animation frames ready to assign.");
        }
        finally
        {
            if (!Application.isBatchMode) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    private static void ConfigureSprites()
    {
        foreach (string path in Directory.GetFiles(Theme + "Sprites", "*.png"))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Sprite not imported: " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 300f;
            importer.spriteBorder = new Vector4(40, 36, 40, 36);
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
    }

    private static TMP_FontAsset CreateFont(string name)
    {
        string path = Theme + "Fonts/" + name + "-Pixel.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing != null) return existing;
        Font source = AssetDatabase.LoadAssetAtPath<Font>(Theme + "Fonts/" + name + ".ttf");
        if (source == null) throw new InvalidOperationException("Font not imported: " + name);
        var font = TMP_FontAsset.CreateFontAsset(source, 32, 2, GlyphRenderMode.RASTER_HINTED,
            1024, 1024, AtlasPopulationMode.Dynamic, false);
        font.name = name + "-Pixel";
        string characters = new string(Enumerable.Range(32, 95).Select(c => (char)c).ToArray())
            + "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя–—…«»№";
        // Optional punctuation may be absent in a font. The complete Russian alphabet is required.
        font.TryAddCharacters(characters, out string missing);
        string alphabet = "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        if (missing.Any(c => alphabet.Contains(c)))
            throw new InvalidOperationException(name + " is missing Cyrillic glyphs: " + missing);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        AssetDatabase.CreateAsset(font, path);
        foreach (Texture2D texture in font.atlasTextures)
        {
            texture.name = font.name + " Atlas";
            texture.filterMode = FilterMode.Point;
            AssetDatabase.AddObjectToAsset(texture, font);
        }
        font.material.name = font.name + " Material";
        AssetDatabase.AddObjectToAsset(font.material, font);
        EditorUtility.SetDirty(font);
        return font;
    }

    private static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Theme + "Sprites/" + name + ".png");

    private static void SetStyle(MenuButtonStyle style, TMP_FontAsset font)
    {
        style.buttonSprite = Sprite("ButtonPrimary");
        style.secondaryButtonSprite = Sprite("ButtonSecondary");
        style.lightButtonSprite = Sprite("ButtonLight");
        style.iconButtonSprite = Sprite("ButtonIcon");
        style.panelSprite = Sprite("PanelDark");
        style.illustrationPanelSprite = Sprite("PanelLight");
        style.font = font;
        style.buttonColor = Color.white;
        style.textColor = new Color32(244, 235, 221, 255);
        style.darkTextColor = new Color32(34, 27, 49, 255);
        style.highlightedColor = new Color32(235, 150, 70, 255);
        style.focusFrameWidth = 3f;
        style.disabledColor = new Color32(156, 151, 167, 160);
        EditorUtility.SetDirty(style);
    }

    private static void StyleHierarchy(GameObject root, MenuButtonStyle style)
    {
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            style.ApplyFont(text);
            text.color = style.textColor;
        }
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            if (button.GetComponent<LevelCardView>() != null)
            {
                button.transition = Selectable.Transition.None;
                if (button.targetGraphic is Image card) SetPlate(card, style.panelSprite);
                var serialized = new SerializedObject(button.GetComponent<LevelCardView>());
                serialized.FindProperty("hoverFrameWidth").floatValue = style.focusFrameWidth;
                serialized.FindProperty("sharedFocusColor").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                continue;
            }
            // Paging dots are indicators, not plate buttons; keep their existing artwork.
            bool dot = button.name.StartsWith("Dot", StringComparison.Ordinal);
            bool primary = new[] { "PlayButton", "ResumeButton", "ContinueButton", "StartButton", "NextLevelButton" }.Contains(button.name);
            bool icon = button.GetComponentsInChildren<TMP_Text>(true).Length == 0 && !dot;
            var plate = icon ? MenuButtonStyle.Plate.Icon : primary ? MenuButtonStyle.Plate.Primary : MenuButtonStyle.Plate.Secondary;
            if (!dot) style.Apply(button, plate);
            else style.ApplyFocus(button);
            var feedback = button.GetComponent<MenuControlFeedback>() ?? button.gameObject.AddComponent<MenuControlFeedback>();
            var data = new SerializedObject(feedback);
            data.FindProperty("plate").enumValueIndex = (int)plate;
            data.FindProperty("sharedFocusColor").boolValue = true;
            data.ApplyModifiedPropertiesWithoutUndo();
            foreach (Image image in button.GetComponentsInChildren<Image>(true))
                if (image != button.targetGraphic && image.name == "Icon") image.color = style.darkTextColor;
        }
        foreach (Image image in root.GetComponentsInChildren<Image>(true))
        {
            if (image.GetComponent<Button>() != null) continue;
            if (new[] { "MainButtonsPanel", "Content", "DefeatPanel", "ScorePanel", "PreviewFrame" }.Contains(image.name))
                SetPlate(image, style.panelSprite);
            if (image.name == "GuidePanel") SetPlate(image, style.panelSprite);
        }
        foreach (Slider slider in root.GetComponentsInChildren<Slider>(true))
        {
            style.ApplyFocus(slider);
            if (slider.GetComponent<MenuControlFeedback>() == null) slider.gameObject.AddComponent<MenuControlFeedback>();
            if (slider.fillRect != null && slider.fillRect.TryGetComponent<Image>(out var fill))
                fill.color = new Color32(166, 140, 207, 255);
            if (slider.targetGraphic is Image handle)
            {
                SetPlate(handle, style.iconButtonSprite);
                handle.pixelsPerUnitMultiplier = 2f;
            }
            foreach (Image image in slider.GetComponentsInChildren<Image>(true))
                if (image.name == "Background") SetPlate(image, style.secondaryButtonSprite);
        }
        foreach (CatProjectile cat in root.GetComponentsInChildren<CatProjectile>(true)) EnsureAnimation(cat.gameObject);
        foreach (MouseEnemy mouse in root.GetComponentsInChildren<MouseEnemy>(true)) EnsureAnimation(mouse.gameObject);
    }

    private static void SetPlate(Image image, Sprite sprite)
    {
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = Color.white;
        image.pixelsPerUnitMultiplier = 1f;
    }

    private static void EnsureAnimation(GameObject target)
    {
        if (target.GetComponent<IdleSpriteAnimation>() == null)
            target.AddComponent<IdleSpriteAnimation>();
    }

    private static void InstallAnimationPrefabs()
    {
        foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Cats", "Assets/Prefabs/Mouses" })
            .Select(AssetDatabase.GUIDToAssetPath))
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<CatProjectile>() != null || root.GetComponent<MouseEnemy>() != null)
                {
                    EnsureAnimation(root);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    private static void CreateTemplates(MenuButtonStyle style)
    {
        Directory.CreateDirectory("Assets/Prefabs/UI/PixelTheme");
        foreach (MenuButtonStyle.Plate plate in Enum.GetValues(typeof(MenuButtonStyle.Plate)))
        {
            var button = new GameObject("Button" + plate, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(MenuControlFeedback));
            try
            {
                var rect = (RectTransform)button.transform;
                rect.sizeDelta = plate == MenuButtonStyle.Plate.Icon ? new Vector2(72, 72) : new Vector2(320, 76);
                button.GetComponent<Button>().targetGraphic = button.GetComponent<Image>();
                if (plate != MenuButtonStyle.Plate.Icon)
                {
                    var label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                    label.transform.SetParent(button.transform, false);
                    var text = label.GetComponent<TextMeshProUGUI>();
                    text.text = "Продолжить";
                    text.fontSize = 32;
                    text.alignment = TextAlignmentOptions.Center;
                    text.raycastTarget = false;
                    text.rectTransform.anchorMin = Vector2.zero;
                    text.rectTransform.anchorMax = Vector2.one;
                    text.rectTransform.offsetMin = new Vector2(16, 6);
                    text.rectTransform.offsetMax = new Vector2(-16, -6);
                }
                style.Apply(button.GetComponent<Button>(), plate);
                var data = new SerializedObject(button.GetComponent<MenuControlFeedback>());
                data.FindProperty("plate").enumValueIndex = (int)plate;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(button, "Assets/Prefabs/UI/PixelTheme/Button" + plate + ".prefab");
            }
            finally { Object.DestroyImmediate(button); }
        }
        foreach (bool light in new[] { false, true })
        {
            var panel = new GameObject(light ? "PanelLight" : "PanelDark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            try
            {
                ((RectTransform)panel.transform).sizeDelta = new Vector2(640, 420);
                SetPlate(panel.GetComponent<Image>(), light ? style.illustrationPanelSprite : style.panelSprite);
                PrefabUtility.SaveAsPrefabAsset(panel, "Assets/Prefabs/UI/PixelTheme/" + panel.name + ".prefab");
            }
            finally { Object.DestroyImmediate(panel); }
        }
    }
}
