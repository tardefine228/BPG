// Copy into the isolated check project's Assets. Exercises Boot/MainMenu only; never loads gameplay.
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class StartupMenuChecks
{
    static StartupMenuChecks() => EditorApplication.playModeStateChanged += Changed;

    public static void Run()
    {
        if (PlayerSettings.companyName != "BPGChecks") throw new InvalidOperationException("Use the isolated project.");
        ValidateTutorial();
        EditorSceneManager.OpenScene("Assets/Scenes/Boot.unity");
        SessionState.SetBool("BPGStartupMenuChecks", true);
        EditorApplication.isPlaying = true;
    }

    // Checks just the saved tutorial UI in Edit Mode; no gameplay simulation or preference changes.
    public static void ValidateTutorial()
    {
        if (PlayerSettings.companyName != "BPGChecks") throw new InvalidOperationException("Use the isolated project.");
        EditorSceneManager.OpenScene("Assets/Scenes/Gameplay.unity");
        var guide = Object.FindAnyObjectByType<FirstLevelGuide>(FindObjectsInactive.Include);
        if (guide == null) throw new Exception("Missing FirstLevelGuide");
        var data = new SerializedObject(guide);
        foreach (string field in new[] { "overlayFade", "firstIllustration", "secondIllustration", "gameplayController", "pauseButton", "tutorialPanel" })
            if (data.FindProperty(field)?.objectReferenceValue == null) throw new Exception("Missing tutorial reference: " + field);
        foreach (string field in new[] { "firstIllustration", "secondIllustration" })
            if (((Image)data.FindProperty(field).objectReferenceValue).sprite == null) throw new Exception("Missing tutorial illustration");
        if (data.FindProperty("comicSprite") != null) throw new Exception("Comic is still part of the tutorial");
        var panel = (GameObject)data.FindProperty("tutorialPanel").objectReferenceValue;
        bool canStart = panel.GetComponentsInChildren<Button>(true).Any(button =>
            Enumerable.Range(0, button.onClick.GetPersistentEventCount()).Any(index =>
                button.onClick.GetPersistentTarget(index) == guide && button.onClick.GetPersistentMethodName(index) == "StartPlaying"));
        if (!canStart) throw new Exception("Tutorial start button lost its callback");
        File.WriteAllText("tutorial-ui-checks.done", "PASS");
        Debug.Log("PASS: tutorial has two illustrations and a valid start button; no comic page; no gameplay launched");
    }

    private static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("BPGStartupMenuChecks", false)) return;
        SessionState.SetBool("BPGStartupMenuChecks", false);
        new GameObject("StartupMenuCheckRunner").AddComponent<StartupMenuCheckRunner>();
    }
}

public sealed class StartupMenuCheckRunner : MonoBehaviour
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<string> report = new List<string>();
    private readonly List<string> errors = new List<string>();
    private readonly List<string> editorWarnings = new List<string>();
    private Mouse mouse;
    private Keyboard keyboard;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;
        Application.logMessageReceived += Log;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        mouse = InputSystem.AddDevice<Mouse>("BPGMenuMouse");
        keyboard = InputSystem.AddDevice<Keyboard>("BPGMenuKeyboard");
    }

    private void Log(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (stack.Contains("UnityEditor.Search.SearchDatabase")) editorWarnings.Add(message + "\n" + stack);
        else errors.Add(message + "\n" + stack);
    }

    private IEnumerator Start()
    {
        var pending = new Stack<IEnumerator>();
        pending.Push(Checks());
        while (pending.Count > 0)
        {
            object current;
            try
            {
                if (!pending.Peek().MoveNext()) { pending.Pop(); continue; }
                current = pending.Peek().Current;
            }
            catch (Exception exception) { errors.Add(exception.ToString()); break; }
            if (current is IEnumerator nested) { pending.Push(nested); continue; }
            yield return current;
        }
        Time.timeScale = 1f;
        Application.logMessageReceived -= Log;
        File.WriteAllLines("startup-menu-checks.txt", report.Concat(errors.Select(error => "FAIL: " + error)));
        File.WriteAllLines("startup-menu-editor-warnings.txt", editorWarnings);
        File.WriteAllText("startup-menu-checks.done", errors.Count == 0 ? "PASS" : "FAIL");
        EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private void Pass(string message) { report.Add("PASS: " + message); Debug.Log(message); }
    private static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, Fields).GetValue(owner);
    private static T[] All<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include);
    private static T Find<T>() where T : Object => All<T>().Single();
    private static Button Action(string method) => All<Button>().First(button => Enumerable.Range(0, button.onClick.GetPersistentEventCount())
        .Any(index => button.onClick.GetPersistentMethodName(index) == method));

    private IEnumerator Until(Func<bool> condition, string description, float timeout = 20f)
    {
        double end = Time.realtimeSinceStartupAsDouble + timeout;
        while (!condition()) { Require(Time.realtimeSinceStartupAsDouble < end, "Timeout: " + description); yield return null; }
    }

    private IEnumerator Checks()
    {
        var startup = Find<StartupController>();
        var settings = Get<StartupSplashSettings>(startup, "settings");
        var cat = Get<Image>(startup, "runningCatImage");
        var branding = Get<CanvasGroup>(startup, "brandingGroup");
        Require(settings.runningCatFrames.Length == 8 && settings.runningCatFrames.All(frame => frame != null), "Missing run frames");
        Require(!Get<Image>(startup, "logoImage").enabled, "Hidden company logo reappeared");
        Require(!cat.raycastTarget, "Running cat intercepts clicks");
        yield return Until(() => cat.enabled, "running cat appears");
        float firstX = cat.rectTransform.anchoredPosition.x;
        var frames = new HashSet<Sprite>();
        double started = Time.realtimeSinceStartupAsDouble;
        float finalX = firstX;
        bool captured = false;
        Time.timeScale = 0f;
        while (cat != null && cat.enabled)
        {
            frames.Add(cat.sprite);
            float x = cat.rectTransform.anchoredPosition.x;
            Require(x >= finalX - 1f, "Cat moves backwards");
            finalX = x;
            Require(branding.alpha > 0.99f, "Cat runs before title is fully visible");
            Require(Vector2.Distance(cat.rectTransform.sizeDelta, cat.sprite.rect.size * settings.runningCatPixelScale) < 0.1f,
                "Cat frames change their pixel scale");
            if (!captured && x >= 0f)
            {
                captured = true;
                yield return Capture("startup-cat");
            }
            yield return null;
        }
        Time.timeScale = 1f;
        Require(frames.Count == 8, "Not all eight running frames were shown");
        Require(finalX - firstX > 1500f, "Cat did not cross the screen");
        Require(Time.realtimeSinceStartupAsDouble - started >= settings.runningCatDuration - 0.15f, "Run cut short");
        Pass("Boot: 8 frames, left-to-right movement, stable pixel scale, unscaled animation, no company logo");
        yield return Until(() => SceneManager.GetActiveScene().name == "MainMenu" && !SceneTransition.IsLoading, "Boot reveals MainMenu", 45f);
        Require(!SceneTransition.StartupVisible && All<StartupController>().Length == 0, "Startup overlay left behind");
        ValidateMenu();
        var title = All<TMP_Text>().Single(text => text.name == "GameName");
        Vector2 titlePosition = title.rectTransform.anchoredPosition;
        Quaternion titleRotation = title.rectTransform.localRotation;
        yield return new WaitForSecondsRealtime(0.5f);
        Require(!title.raycastTarget, "Game title intercepts clicks");
        Require(title.rectTransform.anchoredPosition != titlePosition || title.rectTransform.localRotation != titleRotation, "Title sway does not run");
        yield return Capture("main-menu");
        Pass("MainMenu: valid scripts, button callbacks, one active EventSystem, title sway and nonblocking title");

        yield return Click(Action("OnSettings"));
        yield return new WaitForSecondsRealtime(0.25f);
        var menu = Find<MenuManager>();
        Require(menu.SettingsOpen && !menu.LevelsOpen, "Settings did not open");
        var audioSettings = Find<AudioSettingsController>();
        var audio = Find<AudioManager>();
        var music = Get<Slider>(audioSettings, "musicVolumeSlider");
        var effects = Get<Slider>(audioSettings, "effectsVolumeSlider");
        float savedMusic = audio.MusicVolume, savedEffects = audio.EffectsVolume;
        music.value = 0.31f;
        effects.value = 0.47f;
        Require(Mathf.Abs(audio.MusicVolume - 0.31f) < 0.01f && Mathf.Abs(audio.EffectsVolume - 0.47f) < 0.01f,
            "Volume sliders not connected to AudioManager");
        CheckRaycast(music.handleRect.position, music.gameObject);
        CheckRaycast(effects.handleRect.position, effects.gameObject);
        yield return Capture("menu-settings");
        music.value = savedMusic; effects.value = savedEffects;
        yield return Click(Action("CloseSettings"));
        yield return new WaitForSecondsRealtime(0.25f);
        Require(!menu.SettingsOpen, "Settings did not close");
        Pass("Settings: real mouse clicks open/close, sliders receive input and update audio values");

        yield return Click(Action("OnPlay"));
        yield return new WaitForSecondsRealtime(0.25f);
        Require(menu.LevelsOpen && !menu.SettingsOpen, "Level selection did not open");
        var select = Find<LevelSelectController>();
        select.SetPage(0);
        yield return new WaitForSecondsRealtime(0.4f);
        var next = Get<Button>(select, "nextPageButton");
        var previous = Get<Button>(select, "previousPageButton");
        yield return Capture("menu-levels");
        Require(next.gameObject.activeInHierarchy && !previous.gameObject.activeSelf, "First page arrows incorrect");
        foreach (var card in All<LevelCardView>().Where(card => card.PageIndex == 0 && card.GetComponent<Button>().IsInteractable()))
            CheckRaycast(card.transform.TransformPoint(((RectTransform)card.transform).rect.center), card.gameObject);
        yield return Click(next);
        yield return new WaitForSecondsRealtime(0.4f);
        Require(Get<int>(select, "currentPage") == 1 && previous.gameObject.activeInHierarchy && !next.gameObject.activeSelf,
            "Second page navigation incorrect");
        yield return Click(previous);
        yield return new WaitForSecondsRealtime(0.4f);
        Require(Get<int>(select, "currentPage") == 0, "Previous page did not work");
        yield return Capture("menu-levels");
        yield return Escape();
        yield return new WaitForSecondsRealtime(0.25f);
        Require(!menu.LevelsOpen, "Escape does not close level selection");
        yield return Click(Action("OnSettings"));
        yield return new WaitForSecondsRealtime(0.25f);
        yield return Escape();
        yield return new WaitForSecondsRealtime(0.25f);
        Require(!menu.SettingsOpen && Get<CanvasGroup>(menu, "mainMenuGroup").interactable, "Escape did not restore main menu input");
        CheckRaycast(Action("OnQuit").transform.position, Action("OnQuit").gameObject);
        Pass("Level selection: card input, next/previous pages, Escape closes both menu panels, quit button receives input");
    }

    private void ValidateMenu()
    {
        foreach (var transform in All<Transform>())
            Require(transform.GetComponents<MonoBehaviour>().All(component => component != null), "Missing script on " + transform.name);
        Require(All<EventSystem>().Count(system => system.isActiveAndEnabled) == 1, "Duplicate or missing EventSystem");
        foreach (var button in All<Button>())
        {
            Require(button.onClick.GetPersistentEventCount() > 0, "No OnClick on " + button.name);
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                Object owner = button.onClick.GetPersistentTarget(i);
                string method = button.onClick.GetPersistentMethodName(i);
                Require(owner != null && owner.GetType().GetMethods().Any(info => info.Name == method), "Invalid OnClick: " + button.name);
            }
        }
        foreach (var text in All<TMP_Text>()) Require(text.font != null, "Missing font on " + text.name);
    }

    private static Vector2 ScreenPoint(Vector3 world) => RectTransformUtility.WorldToScreenPoint(null, world);
    private void CheckRaycast(Vector3 world, GameObject expected)
    {
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = ScreenPoint(world) }, hits);
        Require(hits.Count > 0 && (hits[0].gameObject == expected || hits[0].gameObject.transform.IsChildOf(expected.transform)),
            "Input on " + expected.name + " blocked by " + (hits.Count == 0 ? "no raycast hit" : hits[0].gameObject.name)
            + "; point=" + ScreenPoint(world) + "; screen=" + Screen.width + "x" + Screen.height);
    }

    private IEnumerator Click(Button button)
    {
        Canvas.ForceUpdateCanvases();
        var rect = (RectTransform)button.transform;
        Vector3 world = rect.TransformPoint(rect.rect.center);
        Debug.Log("Input check " + button.name + ": anchored=" + rect.anchoredPosition + ", screen=" + ScreenPoint(world)
            + ", canvas=" + ((RectTransform)button.GetComponentInParent<Canvas>().transform).rect + ", culled=" + button.targetGraphic.canvasRenderer.cull);
        CheckRaycast(world, button.gameObject);
        var module = EventSystem.current.GetComponent<InputSystemUIInputModule>();
        Require(module != null && module.isActiveAndEnabled && button.IsInteractable(), "Input module or button unavailable");
        Vector2 position = ScreenPoint(world);
        bool clicked = false;
        UnityEngine.Events.UnityAction callback = () => clicked = true;
        button.onClick.AddListener(callback);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(MouseButton.Left));
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
        yield return Until(() => clicked, "mouse click on " + button.name, 3f);
        button.onClick.RemoveListener(callback);
    }

    private IEnumerator Escape()
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
    }

    private IEnumerator Capture(string name)
    {
        yield return null;
        var camera = Camera.main;
        var canvases = All<Canvas>().Where(canvas => canvas.isRootCanvas && canvas.isActiveAndEnabled).ToArray();
        var modes = canvases.Select(canvas => canvas.renderMode).ToArray();
        var cameras = canvases.Select(canvas => canvas.worldCamera).ToArray();
        var distances = canvases.Select(canvas => canvas.planeDistance).ToArray();
        var orders = canvases.Select(canvas => canvas.sortingOrder).ToArray();
        var target = new RenderTexture(1920, 1080, 24);
        var oldTarget = camera.targetTexture;
        float oldAspect = camera.aspect;
        camera.targetTexture = target; camera.aspect = 16f / 9f;
        foreach (var canvas in canvases)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + 0.1f;
            canvas.sortingOrder = Mathf.Min(32760, 20000 + canvas.sortingOrder);
        }
        Canvas.ForceUpdateCanvases();
        bool oldAsync = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        ShaderUtil.allowAsyncCompilation = oldAsync;
        var active = RenderTexture.active;
        RenderTexture.active = target;
        var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); pixels.Apply();
        File.WriteAllBytes("startup-menu-" + name + ".png", pixels.EncodeToPNG());
        RenderTexture.active = active; camera.targetTexture = oldTarget; camera.aspect = oldAspect;
        for (int i = 0; i < canvases.Length; i++)
        {
            canvases[i].renderMode = modes[i]; canvases[i].worldCamera = cameras[i];
            canvases[i].planeDistance = distances[i]; canvases[i].sortingOrder = orders[i];
        }
        target.Release(); Destroy(target); Destroy(pixels);
        // CanvasScaler restores the Game View layout on the next frame after the offscreen capture.
        yield return null;
    }
}
#endif
