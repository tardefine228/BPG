// Copy into Assets/Editor of the isolated preview project only.
// Editor renders only: no Play Mode, physics simulation, player input or gameplay checks.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public static class VisualPreviewRunner
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        if (PlayerSettings.companyName != "BPGChecks")
            throw new InvalidOperationException("Render only in the isolated preview project.");
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            Directory.CreateDirectory("visual-previews");
            if (Environment.GetCommandLineArgs().Contains("-bpgOnboardingVisuals"))
            {
                RenderOnboardingVisuals();
                File.WriteAllText("visual-previews/onboarding.done", "PASS");
                EditorApplication.Exit(0);
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("-bpgInteractionVisuals"))
            {
                RenderInteractionVisuals();
                File.WriteAllText("visual-previews/interactions.done", "PASS");
                Debug.Log("INTERACTION VISUAL PREVIEWS COMPLETE (edit mode only)");
                EditorApplication.Exit(0);
                return;
            }
            foreach (string name in new[] { "Gameplay", "Level2", "Level3", "Level4", "Level5" })
            {
                EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
                Camera camera = Camera.main;
                camera.aspect = 16f / 9f;
                var framing = camera.GetComponent<GameplayCameraController>();
                typeof(GameplayCameraController).GetMethod("PreviewFrame", Private).Invoke(framing, null);
                // Keep the visual comparison clear of editor-state menu panels.
                foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                    canvas.gameObject.SetActive(false);
                var atmosphere = camera.GetComponent<ForestAtmosphere>();
                atmosphere.enabled = false;
                Capture(camera, name + "-off");
                atmosphere.enabled = true;
                foreach (float time in new[] { 0f, 60f, 120f })
                {
                    typeof(ForestAtmosphere).GetField("animationTime", Private).SetValue(atmosphere, time);
                    typeof(ForestAtmosphere).GetMethod("UpdateOverlay", Private).Invoke(atmosphere, null);
                    Capture(camera, name + "-" + time);
                }
                if (name == "Level2") CaptureSpeed(camera);
            }
            Shader shader = Shader.Find("BPG/ForestAtmosphere");
            var messages = ShaderUtil.GetShaderMessages(shader);
            File.WriteAllLines("visual-previews/shader-messages.txt", messages.Select(m => m.message));
            if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Atmosphere shader compilation failed.");
            File.WriteAllText("visual-previews/render.done", "PASS");
            Debug.Log("VISUAL PREVIEW RENDERS COMPLETE (edit mode only)");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            File.WriteAllText("visual-previews/render.done", exception.ToString());
            EditorApplication.Exit(1);
        }
    }

    private static void Invoke(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static void Set(object target, string field, object value)
        => target.GetType().GetField(field, Private).SetValue(target, value);

    private static void RenderInteractionVisuals()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameplay.unity");
        Camera camera = Camera.main;
        camera.aspect = 16f / 9f;
        Invoke(camera.GetComponent<GameplayCameraController>(), "PreviewFrame");
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            canvas.gameObject.SetActive(false);
        var gun = Object.FindAnyObjectByType<SlingshotController>();
        Set(gun, "mainCamera", camera);
        var cat = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cats/Cat3.prefab"));
        var body = cat.GetComponent<Rigidbody2D>();
        body.simulated = false;
        Set(gun, "projectile", body);
        Invoke(gun, "SetLoadedPose");
        Invoke(gun, "SetLoadedTransform", gun.launchPoint.position, Quaternion.identity);
        Invoke(gun, "CreateShotHints");
        var dots = (List<SpriteRenderer>)typeof(SlingshotController).GetField("dots", Private).GetValue(gun);
        var shadows = (List<SpriteRenderer>)typeof(SlingshotController).GetField("dotShadows", Private).GetValue(gun);
        foreach (var dot in dots)
        {
            var shadow = new GameObject("PreviewDotShadow").AddComponent<SpriteRenderer>();
            shadow.transform.SetParent(dot.transform, false);
            shadow.sprite = dot.sprite;
            shadow.sharedMaterial = dot.sharedMaterial;
            shadow.sortingOrder = dot.sortingOrder - 1;
            shadows.Add(shadow);
        }
        Capture(camera, "hint-loaded", () => Invoke(gun, "LateUpdate"));
        Set(gun, "shotInProgress", true);
        Set(gun, "shotHadAbility", true);
        Set(gun, "abilityUsed", true);
        Set(gun, "showFlightInstruction", true);
        body.transform.SetParent(null);
        body.transform.position = gun.launchPoint.position + new Vector3(12f, 4f, 0f);
        Set(gun, "flightHintAnchor", body.transform);
        Capture(camera, "hint-used-hidden", () => Invoke(gun, "LateUpdate"));
        Set(gun, "shotInProgress", false);
        Invoke(gun, "SetLoadedPose");
        Invoke(gun, "SetLoadedTransform", gun.launchPoint.position, Quaternion.identity);
        Set(gun, "abilityHintShown", true);
        body.gameObject.SetActive(false);
        foreach (int id in new[] { 2, 4, 5 })
        {
            var sample = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cats/Cat" + id + ".prefab"));
            var sampleBody = sample.GetComponent<Rigidbody2D>();
            sampleBody.simulated = false;
            Set(gun, "projectile", sampleBody);
            Invoke(gun, "SetLoadedPose");
            Invoke(gun, "SetLoadedTransform", gun.launchPoint.position, Quaternion.identity);
            Capture(camera, "hint-cat" + id, () => Invoke(gun, "LateUpdate"));
            Object.DestroyImmediate(sample);
        }
        Set(gun, "projectile", body);
        body.gameObject.SetActive(true);
        Set(gun, "isDragging", true);
        foreach (float strength in new[] { 0.2f, 0.95f })
        {
            Capture(camera, "pull-" + strength, () =>
            {
                Vector2 start = camera.WorldToScreenPoint(gun.launchPoint.position);
                Set(gun, "dragStartScreen", start);
                float full = camera.pixelHeight * gun.fullPullScreenFraction;
                Invoke(gun, "Aim", start - new Vector2(strength, strength * 0.7f).normalized * strength * full);
                Invoke(gun, "LateUpdate");
            });
        }
        // Preview the focus frame and squeeze on the actual menu, without invoking onClick.
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        camera = Camera.main;
        camera.aspect = 16f / 9f;
        var controls = Object.FindObjectsByType<MenuControlFeedback>(FindObjectsInactive.Exclude);
        foreach (var feedback in controls) { Invoke(feedback, "Awake"); Invoke(feedback, "OnEnable"); }
        var button = controls.First(item => item.GetComponent<Button>() != null);
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        button.OnPointerEnter(pointer);
        button.GetComponent<Button>().OnPointerEnter(pointer);
        button.GetComponent<Button>().targetGraphic.canvasRenderer.SetColor(button.GetComponent<Button>().colors.highlightedColor);
        Canvas.ForceUpdateCanvases();
        Capture(camera, "button-focus");
        button.OnPointerDown(pointer);
        button.GetComponent<Button>().OnPointerDown(pointer);
        button.GetComponent<Button>().targetGraphic.canvasRenderer.SetColor(button.GetComponent<Button>().colors.pressedColor);
        Capture(camera, "button-pressed");
        foreach (var feedback in controls) Invoke(feedback, "OnDisable");
        var levels = Object.FindAnyObjectByType<LevelSelectController>(FindObjectsInactive.Include);
        var menu = Object.FindAnyObjectByType<MenuManager>(FindObjectsInactive.Include);
        var mainGroup = (CanvasGroup)typeof(MenuManager).GetField("mainMenuGroup", Private).GetValue(menu);
        var levelGroup = (CanvasGroup)typeof(MenuManager).GetField("levelSelectGroup", Private).GetValue(menu);
        mainGroup.gameObject.SetActive(false);
        levelGroup.gameObject.SetActive(true);
        levelGroup.alpha = 1f;
        levelGroup.interactable = true;
        levelGroup.blocksRaycasts = true;
        foreach (var card in Object.FindObjectsByType<LevelCardView>(FindObjectsInactive.Include))
            Invoke(card, "Awake");
        Invoke(levels, "Awake");
        levels.Initialize(menu);
        Invoke(levels, "SetPageImmediate", 0);
        var cardFocus = Object.FindObjectsByType<LevelCardView>(FindObjectsInactive.Exclude).First(card => card.LevelId == 1);
        cardFocus.OnPointerEnter(pointer);
        Capture(camera, "card-focus");
        levelGroup.gameObject.SetActive(false);
        var settingsGroup = (CanvasGroup)typeof(MenuManager).GetField("settingsGroup", Private).GetValue(menu);
        settingsGroup.gameObject.SetActive(true);
        settingsGroup.alpha = 1f;
        settingsGroup.interactable = true;
        settingsGroup.blocksRaycasts = true;
        var slider = Object.FindObjectsByType<Slider>(FindObjectsInactive.Exclude).First();
        var sliderFeedback = slider.GetComponent<MenuControlFeedback>();
        Invoke(sliderFeedback, "Awake");
        sliderFeedback.OnPointerEnter(pointer);
        slider.targetGraphic.canvasRenderer.SetColor(slider.colors.highlightedColor);
        Capture(camera, "slider-focus");
    }

    private static void RenderOnboardingVisuals()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Gameplay.unity");
        Camera camera = Camera.main;
        camera.aspect = 16f / 9f;
        Invoke(camera.GetComponent<GameplayCameraController>(), "PreviewFrame");
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            canvas.gameObject.SetActive(false);
        var guide = Object.FindAnyObjectByType<FirstLevelGuide>(FindObjectsInactive.Include);
        Invoke(guide, "Awake");
        var fade = (PanelFade)typeof(FirstLevelGuide).GetField("overlayFade", Private).GetValue(guide);
        fade.GetComponentInParent<Canvas>(true).gameObject.SetActive(true);
        fade.SetVisible(true, true);
        Invoke(guide, "ShowComicPage");
        Capture(camera, "comic-locked");
        var caption = (TMPro.TMP_Text)typeof(FirstLevelGuide).GetField("comicContinueText", Private).GetValue(guide);
        caption.enabled = true;
        Capture(camera, "comic-ready");
        Capture(camera, "comic-4by3", width: 1440, height: 1080);
        Invoke(guide, "ShowTutorialPage");
        Capture(camera, "comic-to-tutorial");
        RenderInteractionVisuals();
    }

    private static void CaptureSpeed(Camera camera)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cats/Cat2.prefab");
        GameObject cat = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        cat.transform.position = camera.ViewportToWorldPoint(new Vector3(0.34f, 0.48f, -camera.transform.position.z));
        cat.GetComponent<CatProjectile>().ShowLoaded();
        SpriteRenderer sprite = cat.GetComponent<SpriteRenderer>();
        var settings = cat.GetComponent<ProjectileComicEffects>();
        var serialized = new SerializedObject(settings);
        float radius = Mathf.Max(sprite.bounds.extents.x, sprite.bounds.extents.y);
        float scale = radius * serialized.FindProperty("abilityEffectScale").floatValue
            * serialized.FindProperty("speedEffectScale").floatValue;
        var burst = Object.FindAnyObjectByType<ComicParticleBurst>();
        float duration = serialized.FindProperty("speedPulseDuration")?.floatValue ?? 0.32f;
        burst.BeginAbility(sprite.bounds.center, new Vector2(0.2f, -1f), ComicParticleBurst.AbilityEffect.Speed,
            serialized.FindProperty("speedColor").colorValue, scale, 0f, sprite.sortingLayerID,
            sprite.sortingOrder + 2, serialized.FindProperty("comicMaterial").objectReferenceValue as Material,
            duration, serialized.FindProperty("abilityIntensity").floatValue,
            serialized.FindProperty("abilityStrokeWidth").floatValue, cat.transform);
        foreach (ParticleSystem particles in burst.GetComponentsInChildren<ParticleSystem>())
            particles.Simulate(0.04f, false, false, false);
        typeof(ComicParticleBurst).GetMethod("DrawPulse", Private).Invoke(burst, new object[] { 0.12f });
        Capture(camera, "speed-full-frame");
        camera.orthographicSize = 6f;
        camera.transform.position = new Vector3(sprite.bounds.center.x, sprite.bounds.center.y, camera.transform.position.z);
        typeof(ForestAtmosphere).GetMethod("UpdateOverlay", Private).Invoke(camera.GetComponent<ForestAtmosphere>(), null);
        Capture(camera, "speed-closeup");
    }

    private static void Capture(Camera camera, string name, Action prepare = null, int width = 1920, int height = 1080)
    {
        var target = new RenderTexture(width, height, 24);
        target.Create();
        var oldTarget = camera.targetTexture;
        float oldAspect = camera.aspect;
        var oldActive = RenderTexture.active;
        camera.targetTexture = target;
        camera.aspect = (float)width / height;
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude)
            .Where(canvas => canvas.isRootCanvas && canvas.isActiveAndEnabled).ToArray();
        var modes = canvases.Select(canvas => canvas.renderMode).ToArray();
        var cameras = canvases.Select(canvas => canvas.worldCamera).ToArray();
        var orders = canvases.Select(canvas => canvas.sortingOrder).ToArray();
        var distances = canvases.Select(canvas => canvas.planeDistance).ToArray();
        foreach (var canvas in canvases)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + 0.1f;
            canvas.sortingOrder = 20000 + canvas.sortingOrder;
        }
        Canvas.ForceUpdateCanvases();
        prepare?.Invoke();
        Canvas.ForceUpdateCanvases();
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes("visual-previews/" + name + ".png", image.EncodeToPNG());
        RenderTexture.active = oldActive;
        camera.targetTexture = oldTarget;
        camera.aspect = oldAspect;
        for (int i = 0; i < canvases.Length; i++)
        {
            canvases[i].renderMode = modes[i];
            canvases[i].worldCamera = cameras[i];
            canvases[i].sortingOrder = orders[i];
            canvases[i].planeDistance = distances[i];
        }
        target.Release();
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(image);
    }
}
