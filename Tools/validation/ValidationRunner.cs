using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;

public sealed class ValidationRunner : MonoBehaviour
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly List<string> results = new List<string>();
    private readonly List<string> errors = new List<string>();
    private readonly List<string> editorErrors = new List<string>();
    private Mouse validationMouse;
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.logMessageReceived += Log;
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        validationMouse = InputSystem.AddDevice<Mouse>("BPGChecksMouse");
    }
    private void Log(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (stack.Contains("UnityEditor.Search.SearchDatabase") || stack.Contains("Unity.AI.Tracing.ConsoleSink"))
            editorErrors.Add(message + "\n" + stack);
        else errors.Add(message + "\n" + stack);
    }
    private IEnumerator Start()
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(Checks());
        while (stack.Count > 0)
        {
            object current = null;
            try
            {
                if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                current = stack.Peek().Current;
            }
            catch (Exception exception) { errors.Add(exception.ToString()); break; }
            if (current is IEnumerator nested) { stack.Push(nested); continue; }
            yield return current;
        }
        Application.logMessageReceived -= Log;
        File.WriteAllLines("play-checks.txt", results.Concat(errors.Select(error => "FAIL: " + error)));
        File.WriteAllLines("editor-checks-warnings.txt", editorErrors);
        File.WriteAllText("play-checks.done", errors.Count == 0 ? "PASS" : "FAIL");
        Debug.Log("PLAY CHECKS " + (errors.Count == 0 ? "PASS" : "FAIL"));
        UnityEditor.EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
    }
    private void Pass(string message) { results.Add(message); Debug.Log("CHECK OK: " + message); }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object target, string field) => target.GetType().GetField(field, Flags).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Flags).SetValue(target, value);
    private static T Find<T>() where T : Object => Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
    private static T[] All<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include);
    private static Button Action(string method) => All<Button>().First(button => Enumerable.Range(0, button.onClick.GetPersistentEventCount())
        .Any(index => button.onClick.GetPersistentMethodName(index) == method));
    private IEnumerator Until(Func<bool> condition, string description, float timeout = 30f)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (!condition()) { Require(Time.realtimeSinceStartup < end, "Timeout: " + description); yield return null; }
    }
    private IEnumerator Scene(string name)
    {
        yield return Until(() => SceneManager.GetActiveScene().name == name && !SceneTransition.IsLoading, "scene " + name, 60f);
        yield return null;
    }
    private IEnumerator Capture(string name)
    {
        yield return null;
        Camera camera = Camera.main;
        if (camera == null) yield break;
        Canvas[] canvases = All<Canvas>().Where(canvas => canvas.isRootCanvas && canvas.isActiveAndEnabled).ToArray();
        var modes = canvases.Select(canvas => canvas.renderMode).ToArray();
        var cameras = canvases.Select(canvas => canvas.worldCamera).ToArray();
        var distances = canvases.Select(canvas => canvas.planeDistance).ToArray();
        var target = new RenderTexture(1920,1080,24);
        var previousTarget = camera.targetTexture;
        float aspect = camera.aspect;
        camera.targetTexture = target;
        camera.aspect = 16f/9f;
        foreach (Canvas canvas in canvases)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + 0.1f;
        }
        Canvas.ForceUpdateCanvases();
        bool asyncCompilation = UnityEditor.ShaderUtil.allowAsyncCompilation;
        UnityEditor.ShaderUtil.allowAsyncCompilation = false;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        UnityEditor.ShaderUtil.allowAsyncCompilation = asyncCompilation;
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var pixels = new Texture2D(1920,1080,TextureFormat.RGB24,false);
        pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);
        pixels.Apply();
        File.WriteAllBytes("validation-" + name + ".png", pixels.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = previousTarget;
        camera.aspect = aspect;
        for (int i=0; i<canvases.Length; i++)
        {
            canvases[i].renderMode = modes[i];
            canvases[i].worldCamera = cameras[i];
            canvases[i].planeDistance = distances[i];
        }
        target.Release(); Destroy(target); Destroy(pixels);
        yield return null;
    }

    private void CheckCameraSelection(GameplayCameraController camera, string selectedField)
    {
        foreach (string field in new[] { "launcherButton", "overviewButton", "targetButton" })
        {
            var button = (Button)Get(camera, field);
            Color original = (Color)Get(camera, field + "Color");
            Color expected = original * (field == selectedField ? (Color)Get(camera, "selectedViewTint") : Color.white);
            Require(button.targetGraphic.color == expected, field + ": incorrect camera selection highlight");
            Require(button.targetGraphic.canvasRenderer.GetColor().a > 0.99f,
                field + ": disabled button became translucent");
        }
    }

    private IEnumerator CheckCameraButtons()
    {
        var camera = Find<GameplayCameraController>();
        CheckCameraSelection(camera, "overviewButton");
        yield return Click(Action("FocusLauncher"));
        yield return Until(() => !camera.IsBusy, "launcher focus");
        CheckCameraSelection(camera, "launcherButton");
        yield return Click(Action("FocusTarget"));
        yield return Until(() => !camera.IsBusy, "target focus");
        CheckCameraSelection(camera, "targetButton");
        yield return Click(Action("OpenPauseMenu"));
        yield return new WaitForSecondsRealtime(0.3f);
        Require(Time.timeScale == 0f && ((GameObject)Get(camera, "controlsCanvas")).activeInHierarchy,
            "Camera buttons disappeared on pause");
        var pause = Find<PauseMenuController>();
        var overlay = (PanelFade)Get(pause, "overlayFade");
        Require(overlay.gameObject.activeInHierarchy && overlay.GetComponent<CanvasGroup>().alpha == 1f
            && ((GameObject)Get(pause, "pausePage")).activeInHierarchy, "Pause panel is invisible");
        foreach (string field in new[] { "launcherButton", "overviewButton", "targetButton" })
            Require(!((Button)Get(camera, field)).IsInteractable(), field + ": usable while paused");
        CheckCameraSelection(camera, "targetButton");
        yield return Capture("camera-selection-paused");
        // Даже вызов обработчика напрямую не должен менять камеру на паузе.
        camera.FocusLauncher();
        yield return null;
        CheckCameraSelection(camera, "targetButton");
        yield return Click(Action("ResumeGame"));
        yield return new WaitForSecondsRealtime(0.3f);
        yield return Click(Action("ShowOverview"));
        yield return Until(() => !camera.IsBusy, "overview focus");
        CheckCameraSelection(camera, "overviewButton");
        Require(All<Transform>().All(transform => transform.name != "AimHint"), "Removed camera hint returned");
        Pass(SceneManager.GetActiveScene().name + ": camera selection survives pause; buttons remain visible, opaque and blocked; hint absent");
    }

    private IEnumerator Click(Button button)
    {
        Canvas.ForceUpdateCanvases();
        var system = EventSystem.current;
        Require(system != null, "No active EventSystem");
        var module = system.GetComponent<InputSystemUIInputModule>();
        Require(module != null && module.isActiveAndEnabled && module.point.action.enabled
            && module.leftClick.action.enabled, "UI input actions disabled in " + SceneManager.GetActiveScene().name);
        Require(button.IsInteractable(), button.name + ": button is not interactable");
        var rect = (RectTransform)button.transform;
        var canvas = button.GetComponentInParent<Canvas>();
        Vector2 position = RectTransformUtility.WorldToScreenPoint(
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
            rect.TransformPoint(rect.rect.center));
        var hits = new List<RaycastResult>();
        system.RaycastAll(new PointerEventData(system) { position = position }, hits);
        Require(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button,
            button.name + ": click blocked by " + (hits.Count > 0 ? hits[0].gameObject.name : "no raycast hit"));
        bool clicked = false;
        UnityEngine.Events.UnityAction callback = () => clicked = true;
        button.onClick.AddListener(callback);
        var mouse = validationMouse;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(MouseButton.Left));
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
        yield return Until(() => clicked, "mouse click on " + button.name, 3f);
        button.onClick.RemoveListener(callback);
    }

    private void CheckCats(int expected)
    {
        var slingshot = Find<SlingshotController>();
        var loaded = (Rigidbody2D)Get(slingshot, "projectile");
        var waiting = (List<Rigidbody2D>)Get(slingshot, "waitingCats");
        Require(Find<LevelSession>().RemainingShots == expected && waiting.Count == expected - 1
            && All<CatProjectile>().Length == expected, "Wrong initial cat count");
        Require(loaded != null && !loaded.simulated && !loaded.GetComponent<CatProjectile>().IsQueued,
            "First cat is not loaded");
        var cats = new[] { loaded }.Concat(waiting).ToArray();
        for (int i = 0; i < cats.Length; i++)
        {
            Require(cats[i].name == "Cat" + (i + 1) + "(Clone)", "Cat prefab order incorrect");
            var cat = cats[i].GetComponent<CatProjectile>();
            var queueSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            var launchSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            Set(cat, "queueSprite", queueSprite);
            Set(cat, "launchSprite", launchSprite);
            if (i == 0) cat.ShowLoaded(); else cat.ShowQueued();
            Require(cat.GetComponent<SpriteRenderer>().sprite == (i == 0 ? launchSprite : queueSprite),
                "Cat pose sprite incorrect");
            Require(!cats[i].simulated && cat.IsQueued == (i > 0), "Waiting cat participates in physics");
            if (i > 1)
                Require(cats[i].position.x < cats[i - 1].position.x, "Cat queue does not extend behind launcher");
        }
        slingshot.Reload();
        Require((Rigidbody2D)Get(slingshot, "projectile") == loaded && slingshot.WaitingCatCount == expected - 1,
            "Reload replaces a cat or changes the queue");
        Require(Vector3.Distance(loaded.transform.position, slingshot.launchPoint.position) < 0.001f,
            "Loaded cat is outside the catapult");
    }

    private IEnumerator CatQueueChecks()
    {
        foreach (var level in LevelProgress.Levels)
        {
            yield return Scene("MainMenu");
            if (!Find<MenuManager>().LevelsOpen)
            {
                yield return Click(Action("OnPlay"));
                yield return new WaitForSecondsRealtime(0.3f);
            }
            var select = Find<LevelSelectController>();
            LevelProgress.UnlockAllLevels = true;
            var card = All<LevelCardView>().First(item => item.LevelId == level.Id);
            card.Refresh((Sprite)Get(select, "starOnSprite"), (Sprite)Get(select, "starOffSprite"));
            select.SetPage(card.PageIndex);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Click(card.GetComponent<Button>());
            yield return Scene(level.SceneName);
            int ammo = Mathf.Min(level.Id + 1, 5);
            CheckCats(ammo);
            yield return CheckCameraButtons();
            if (level.Id == 1 || level.Id == 5)
            {
                yield return Click(Action("FocusLauncher"));
                yield return Until(() => !Find<GameplayCameraController>().IsBusy, "cat queue close-up");
                yield return Capture("cats-level-" + level.Id);
            }
            var slingshot = Find<SlingshotController>();
            slingshot.gameplayCamera = null;
            slingshot.spawnDelay = 0.02f;
            slingshot.projectileLifetime = 0.8f;
            Set(slingshot, "catLoadDuration", 0.12f);
            for (int shot = 1; shot <= ammo; shot++)
            {
                var fired = (Rigidbody2D)Get(slingshot, "projectile");
                var waiting = (List<Rigidbody2D>)Get(slingshot, "waitingCats");
                var next = waiting.Count > 0 ? waiting[0] : null;
                var moved = waiting.Skip(1).ToArray();
                var firedSprite = fired.GetComponent<SpriteRenderer>().sprite;
                var nextSprite = next != null ? (Sprite)Get(next.GetComponent<CatProjectile>(), "launchSprite") : null;
                Set(slingshot, "launchVelocity", new Vector2(-5f, 0f));
                slingshot.StartCoroutine((IEnumerator)typeof(SlingshotController).GetMethod("LaunchRoutine", Flags).Invoke(slingshot, null));
                yield return Until(() => slingshot.ShotsFired == shot && (Rigidbody2D)Get(slingshot, "projectile") == next,
                    "cat transferred from queue");
                Require(fired != null && fired.simulated && fired.GetComponent<SpriteRenderer>().sprite == firedSprite,
                    "Flight replaced the loaded cat or its sprite");
                Require(slingshot.WaitingCatCount == Mathf.Max(0, ammo - shot - 1)
                    && Find<LevelSession>().RemainingShots == ammo - shot, "Queue/ammo did not decrease");
                if (next != null)
                {
                    Require(!next.simulated && !next.GetComponent<CatProjectile>().IsQueued
                        && next.GetComponent<SpriteRenderer>().sprite == nextSprite, "Next cat did not switch to launch sprite");
                    // Пауза во время пересадки останавливает кота и сохраняет очередь.
                    if (shot == 1)
                    {
                        yield return Click(Action("OpenPauseMenu"));
                        var position = next.transform.position;
                        yield return new WaitForSecondsRealtime(0.2f);
                        Require(Time.timeScale == 0f && Vector3.Distance(position, next.transform.position) < 0.001f,
                            "Cat moved while paused");
                        yield return Click(Action("ResumeGame"));
                    }
                }
                yield return Until(() => !slingshot.IsShotInProgress, "cat shot settled");
                if (next != null)
                    Require(Vector3.Distance(next.transform.position, slingshot.launchPoint.position) < 0.001f,
                        "Next cat did not arrive in the catapult");
                for (int i = 0; i < moved.Length; i++)
                    Require(Vector3.Distance(moved[i].transform.position,
                        ((Transform)Get(slingshot, "queueOrigin")).position + (Vector3)((Vector2)Get(slingshot, "queueSpacing") * i)) < 0.001f,
                        "Waiting cats did not advance");
            }
            Require(!slingshot.CanAim && slingshot.WaitingCatCount == 0, "Cat supply was not exhausted");
            yield return Until(() => ((GameObject)Get(Find<LevelSession>(), "defeatOverlay")).activeSelf, "cat exhaustion defeat");
            Find<LevelSession>().RestartLevel();
            yield return Until(() => !SceneTransition.IsLoading && Find<LevelSession>().RemainingShots == ammo, "cat queue restart");
            yield return null;
            CheckCats(ammo);
            yield return Click(Action("OpenPauseMenu"));
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Click(Action("ReturnToMainMenu"));
            yield return Scene("MainMenu");
            Pass(level.SceneName + ": real menu/pause/camera clicks, all " + ammo + " cat launches, two poses, queue movement, pause during loading, exhaustion and restart passed");
        }
    }

    private IEnumerator PauseReturnChecks()
    {
        yield return Scene("MainMenu");
        Require(!Find<MenuManager>().LevelsOpen, "Boot unexpectedly opened level selection");
        foreach (var level in LevelProgress.Levels)
        {
            SceneTransition.Load(level.SceneName);
            yield return Scene(level.SceneName);
            Action("OpenPauseMenu").onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.3f);
            Require(Time.timeScale == 0f, level.SceneName + ": pause failed");
            var exit = Action("ReturnToMainMenu");
            exit.onClick.Invoke();
            exit.onClick.Invoke();
            yield return Scene("MainMenu");
            var menu = Find<MenuManager>();
            var levels = (CanvasGroup)Get(menu, "levelSelectGroup");
            var main = (CanvasGroup)Get(menu, "mainMenuGroup");
            Require(menu.LevelsOpen && !menu.SettingsOpen && levels.gameObject.activeInHierarchy
                && levels.alpha == 1f && levels.interactable && levels.blocksRaycasts,
                level.SceneName + ": level selection missing after pause return");
            Require(Time.timeScale == 1f && !main.interactable && !main.blocksRaycasts,
                level.SceneName + ": menu state incorrect after pause return");
            if (level.Id == 1) yield return Capture("pause-return-levels");
            Action("ReturnToMainMenu").onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.3f);
            Require(!menu.LevelsOpen && main.interactable && main.blocksRaycasts,
                level.SceneName + ": Back button failed after pause return");
            Pass(level.SceneName + ": pause exit opens usable level selection immediately; double click and Back work");
        }
        SceneTransition.Load("MainMenu");
        yield return Scene("MainMenu");
        Require(!Find<MenuManager>().LevelsOpen, "Pause return request leaked into later menu loads");
        Pass("Normal Boot/menu loads keep the main page; pause navigation request is consumed once");
    }

    private IEnumerator Checks()
    {
        if (Environment.GetCommandLineArgs().Contains("-bpgPauseReturnOnly"))
        {
            yield return PauseReturnChecks();
            yield break;
        }
        yield return Scene("MainMenu");
        Require(!SceneTransition.StartupVisible, "Boot remains visible");
        Pass("Boot -> MainMenu completed with loading overlay hidden");
        yield return Capture("main-menu");
        MenuManager menu = Find<MenuManager>();
        var ids = new HashSet<Button>(All<Button>());
        Action("OnSettings").onClick.Invoke();
        yield return new WaitForSecondsRealtime(0.4f);
        Require(menu.SettingsOpen, "Settings button failed");
        yield return Capture("settings");
        var audioSettings = Find<AudioSettingsController>();
        ((Slider)Get(audioSettings, "musicVolumeSlider")).value = 0.23f;
        ((Slider)Get(audioSettings, "effectsVolumeSlider")).value = 0.34f;
        Require(Mathf.Approximately(AudioManager.Instance.MusicVolume, 0.23f) && Mathf.Approximately(AudioManager.Instance.EffectsVolume, 0.34f), "Volume binding failed");
        Action("CloseSettings").onClick.Invoke();
        yield return new WaitForSecondsRealtime(0.4f);
        Action("OnPlay").onClick.Invoke();
        yield return new WaitForSecondsRealtime(0.4f);
        var select = Find<LevelSelectController>();
        Require(menu.LevelsOpen, "Play button failed");
        select.SetPage(1);
        yield return new WaitForSecondsRealtime(0.4f);
        Require((int)Get(select, "currentPage") == 1, "Pagination failed");
        select.ReturnToMainMenu();
        yield return new WaitForSecondsRealtime(0.4f);
        Action("OnPlay").onClick.Invoke();
        yield return new WaitForSecondsRealtime(0.4f);
        Require(ids.SetEquals(All<Button>()), "Menu buttons were replaced");
        Pass("Settings, volumes, pagination and reopening preserve all menu button objects");
        yield return Capture("levels");
        yield return Click(All<LevelCardView>().First(card => card.LevelId == 1).GetComponent<Button>());
        yield return Scene("Gameplay");
        yield return new WaitForSecondsRealtime(0.4f);
        yield return Capture("gameplay");
        yield return CheckCameraButtons();
        CheckCats(2);
        int particleSystems = All<ParticleSystem>().Length;
        int gameObjects = All<Transform>().Length;
        var material = Find<SlingshotController>().projectilePrefab.GetComponent<ProjectileComicEffects>();
        var source = (Material)Get(material, "comicMaterial");
        for (int i = 0; i < 6; i++)
        {
            var watch = Stopwatch.StartNew();
            ComicParticleBurst.Play(source, Vector3.zero, Vector2.up, true, Color.white, Color.yellow, Color.yellow, 9, 7, 1.5f, 0, 20);
            watch.Stop();
            results.Add("pooled-impact " + i + ": " + watch.Elapsed.TotalMilliseconds.ToString("F3") + " ms");
        }
        for (int i = 0; i < 100; i++)
            ComicParticleBurst.Play(source, Vector3.zero, Vector2.up, true, Color.white, Color.yellow, Color.yellow, 9, 7, 1.5f, 0, 20);
        Require(All<ParticleSystem>().Length == particleSystems && All<Transform>().Length == gameObjects, "Impact creates new objects");
        Pass("106 impacts reuse the same 12 bursts / 36 particle systems without new GameObjects");

        var camera = Find<GameplayCameraController>();
        camera.transitionDuration = 0.02f; camera.preShotHold = 0.01f;
        yield return camera.PrepareForShot();
        float size = Camera.main.orthographicSize;
        camera.FollowShot((Rigidbody2D)Get(Find<SlingshotController>(), "projectile"));
        camera.RecordImpact(new Vector2(20f, 0f), null);
        yield return new WaitForSeconds(0.2f);
        Require(Mathf.Abs(Camera.main.orthographicSize - size) < 0.001f, "Impact added zoom");
        yield return Until(() => !camera.IsBusy, "camera return");
        Pass("Camera impact transition retains the flight zoom and returns to overview");

        ids = new HashSet<Button>(All<Button>());
        var pause = Find<PauseMenuController>();
        Action("OpenPauseMenu").onClick.Invoke();
        yield return new WaitForSecondsRealtime(0.3f);
        Require(Time.timeScale == 0f, "Pause failed");
        yield return Capture("pause");
        Action("OpenSettings").onClick.Invoke();
        yield return new WaitForSecondsRealtime(0.3f);
        Action("ReturnToPausePage").onClick.Invoke();
        yield return new WaitForSecondsRealtime(0.3f);
        Action("ResumeGame").onClick.Invoke();
        Require(Time.timeScale == 1f, "Resume failed");
        Require(ids.SetEquals(All<Button>()), "Pause buttons replaced");
        Pass("Pause -> settings -> resume works with the same buttons");

        var slingshot = Find<SlingshotController>();
        slingshot.gameplayCamera = null; slingshot.spawnDelay = 0.02f; slingshot.projectileLifetime = 0.8f;
        Set(slingshot, "catLoadDuration", 0.02f);
        int ammo = Find<LevelSession>().MaxProjectiles;
        for (int shot = 1; shot <= ammo; shot++)
        {
            Require(slingshot.CanAim, "Projectile missing for shot " + shot);
            Rigidbody2D loaded = (Rigidbody2D)Get(slingshot, "projectile");
            Rigidbody2D next = shot < ammo ? ((List<Rigidbody2D>)Get(slingshot, "waitingCats"))[0] : null;
            Set(slingshot, "launchVelocity", new Vector2(-5f, 0f));
            slingshot.StartCoroutine((IEnumerator)typeof(SlingshotController).GetMethod("LaunchRoutine", Flags).Invoke(slingshot, null));
            yield return null;
            Require(loaded.simulated && !loaded.GetComponent<CatProjectile>().IsQueued,
                "Loaded cat did not become the flying projectile");
            Require((Rigidbody2D)Get(slingshot, "projectile") == next, "Next cat was replaced on loading");
            Require(slingshot.WaitingCatCount == Mathf.Max(0, ammo - shot - 1), "Queue did not shrink on firing");
            yield return Until(() => !slingshot.IsShotInProgress, "shot " + shot);
            Require(slingshot.ShotsFired == shot, "Incorrect shot count");
        }
        Require(!slingshot.CanAim && Find<LevelSession>().RemainingShots == 0, "Extra shot allowed");
        slingshot.StartCoroutine((IEnumerator)typeof(SlingshotController).GetMethod("LaunchRoutine", Flags).Invoke(slingshot, null));
        yield return null;
        Require(slingshot.ShotsFired == ammo, "Extra shot fired");
        yield return Until(() => ((GameObject)Get(Find<LevelSession>(), "defeatOverlay")).activeSelf, "defeat");
        Require(Time.timeScale == 0f, "Defeat did not stop gameplay");
        Pass("Two real cat launches exhaust Level1 ammunition; queue shrinks, cat identity survives loading/flight, extra shot is blocked");
        yield return Capture("defeat");
        Action("RestartLevel").onClick.Invoke();
        yield return Until(() => !SceneTransition.IsLoading && Find<LevelSession>().RemainingShots == ammo, "restart");
        yield return null;
        Require(Time.timeScale == 1f && !((GameObject)Get(Find<LevelSession>(), "defeatOverlay")).activeSelf, "Restart state not reset");
        CheckCats(ammo);
        Pass("Defeat restart restores the full cat queue and normal time");

        slingshot = Find<SlingshotController>();
        Set(slingshot, "<ShotsFired>k__BackingField", ammo);
        foreach (var target in All<LevelTarget>()) Set(target, "<IsCleared>k__BackingField", true);
        yield return Until(() => ((GameObject)Get(Find<LevelSession>(), "completionOverlay")).activeSelf, "victory on final shot");
        Require(!((GameObject)Get(Find<LevelSession>(), "defeatOverlay")).activeSelf && LevelProgress.IsCompleted(1), "Final-shot victory lost to defeat");
        Pass("Victory on the final projectile takes priority over defeat and saves progress");
        yield return Capture("victory");
        Action("GoToNextLevel").onClick.Invoke();
        yield return Scene("Level2");
        yield return CheckCameraButtons();
        Require(LevelProgress.CurrentLevelId == 2 && Find<LevelSession>().RemainingShots == 3, "Next level failed");
        CheckCats(3);
        Pass("Victory Continue button opens Level2 with three cats");

        yield return new WaitForSeconds(1f);
        var reinforced = All<LevelTarget>().First(target => target.IsReinforced);
        Rigidbody2D targetBody = reinforced.GetComponent<Rigidbody2D>();
        targetBody.bodyType = RigidbodyType2D.Kinematic;
        targetBody.linearVelocity = Vector2.zero;
        targetBody.angularVelocity = 0f;
        Collider2D boxCollider = reinforced.GetComponent<Collider2D>();
        for (int hit = 1; hit <= reinforced.HitsToClear; hit++)
        {
            Vector3 start = new Vector3(boxCollider.bounds.min.x - 2f, boxCollider.bounds.center.y, 0f);
            Rigidbody2D shot = Instantiate(Find<SlingshotController>().projectilePrefab, start, Quaternion.identity);
            shot.gravityScale = 0f; shot.linearVelocity = new Vector2(15f, 0f);
            shot.GetComponent<ProjectileComicEffects>().BeginFlight();
            yield return Until(() => reinforced.HitsTaken >= hit, "reinforced hit " + hit);
            Require(All<ParticleSystem>().Any(system => system.particleCount > 0), "Collision FX missing");
            if (hit == 1)
            {
                Camera.main.transform.position = new Vector3(reinforced.transform.position.x, reinforced.transform.position.y + 2f, -10f);
                Camera.main.orthographicSize = 7f;
                yield return Capture("reinforced-hit");
            }
            Destroy(shot.gameObject);
            yield return new WaitForSeconds(0.4f);
        }
        Require(reinforced.IsCleared, "Reinforced box did not clear");
        Pass("Level2 reinforced box receives physical projectile hits, emits FX and clears at its configured durability");

        foreach (string level in new[] { "Level3", "Level4", "Level5" })
        {
            SceneTransition.Load(level);
            yield return Scene(level);
            yield return CheckCameraButtons();
            int expectedAmmo = level == "Level3" ? 4 : 5;
            Require(Find<LevelSession>().RemainingShots == expectedAmmo && Find<SlingshotController>().CanAim, level + " initialization failed");
            CheckCats(expectedAmmo);
            Pass(level + " loads with intact UI, FX pool and " + expectedAmmo + " cats");
        }
        slingshot = Find<SlingshotController>();
        Set(slingshot, "<ShotsFired>k__BackingField", 1);
        foreach (var target in All<LevelTarget>()) Set(target, "<IsCleared>k__BackingField", true);
        yield return Until(() => ((GameObject)Get(Find<LevelSession>(), "completionOverlay")).activeSelf, "last level victory");
        Action("GoToNextLevel").onClick.Invoke();
        yield return Scene("MainMenu");
        Pass("Continue after the last implemented level returns to MainMenu");
        yield return PauseReturnChecks();
        yield return CatQueueChecks();
        Require(errors.Count == 0, "Runtime produced errors");
    }
}
