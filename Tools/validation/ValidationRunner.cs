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
        if (Camera.main != null) Camera.main.aspect = 16f / 9f;
        yield return null;
        if (!Environment.GetCommandLineArgs().Contains("-bpgPresentationOnly") && name == "Gameplay")
        {
            yield return null;
            if (FirstLevelGuide.IsOpen)
            {
                Find<FirstLevelGuide>().StartPlaying();
                yield return Until(() => !FirstLevelGuide.IsOpen, "guide auto acceptance");
            }
        }
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



    private IEnumerator CheckCameraButtons()
    {
        var camera = Find<GameplayCameraController>();
        yield return Until(() => !camera.IsBusy, "intro zoom completed");
        Require(All<Transform>().All(item => item.name != "GameplayCameraCanvas"), "Camera controls still exist");
        Vector3 position = camera.transform.position;
        float size = Camera.main.orthographicSize;
        yield return Click(Action("OpenPauseMenu"));
        yield return new WaitForSecondsRealtime(0.3f);
        Require(Time.timeScale == 0f && camera.transform.position == position
            && Camera.main.orthographicSize == size, "Pause changed the fixed camera");
        yield return Click(Action("ResumeGame"));
        yield return new WaitForSecondsRealtime(0.35f);
        Pass(SceneManager.GetActiveScene().name + ": fixed camera, no view controls, pause works");
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
            var slots = (Transform[])Get(slingshot, "queueSlots");
            if (i > 0 && slots != null && i - 1 < slots.Length)
                Require(Mathf.Abs(cats[i].position.x - slots[i - 1].position.x) < 0.01f,
                    "Cat queue ignores authored positions");
            else if (i > 1)
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
                yield return Capture("cats-level-" + level.Id);
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
            Pass(level.SceneName + ": real menu/pause clicks, all " + ammo + " cat launches, two poses, queue movement, pause during loading, exhaustion and restart passed");
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

    private IEnumerator CatFlightChecks()
    {
        yield return TilemapAndRollingChecks();
    }


    private static void CheckLoadingLayout(SceneTransition transition)
    {
        var caption = (TMPro.TMP_Text)Get(transition, "caption");
        var percent = (TMPro.TMP_Text)Get(transition, "percent");
        var fill = (Image)Get(transition, "progressFill");
        var track = (RectTransform)fill.transform.parent;
        Require(caption.rectTransform.anchoredPosition == new Vector2(0f, 30f)
            && Mathf.Abs(caption.fontSize - 54f) < 0.01f, "Loading caption differs from Gameplay layout");
        Require(track.anchoredPosition == new Vector2(0f, -61f)
            && Mathf.Abs(track.sizeDelta.x - 680f) < 0.01f && Mathf.Abs(track.sizeDelta.y - 24.8765f) < 0.01f,
            "Loading bar differs from Gameplay layout");
        Require(Mathf.Abs(percent.rectTransform.anchoredPosition.y + 123.43799f) < 0.01f
            && Mathf.Abs(percent.fontSize - 28f) < 0.01f, "Loading percent differs from Gameplay layout");
        Require(transition.GetComponentsInChildren<Transform>(true).All(child =>
            !new[] { "AccentLine", "LogoPlaceholder", "GameTitle", "StudioText" }.Contains(child.name)),
            "Removed objects reappeared on the loading panel");
    }

    private IEnumerator CheckLoadingRoute(string destination)
    {
        string source = SceneManager.GetActiveScene().name;
        var outgoing = Find<SceneTransition>();
        CheckLoadingLayout(outgoing);
        var group = (CanvasGroup)Get(outgoing, "group");
        SceneTransition.Load(destination);
        yield return Until(() => group.alpha == 1f && ((Image)Get(outgoing, "progressFill")).rectTransform.anchorMax.x >= 0.15f,
            "outgoing loading screen fully rendered");
        Require(group.gameObject.activeInHierarchy && group.blocksRaycasts, "Outgoing loading screen is invisible / does not block clicks");
        Require(((Image)Get(outgoing, "backgroundImage")).canvasRenderer.GetInheritedAlpha() == 1f,
            "Loading background is not opaque");
        yield return Capture("loading-" + source + "-to-" + destination);
        yield return Until(() => SceneManager.GetActiveScene().name == destination, "incoming scene activation");
        var incoming = Find<SceneTransition>();
        CheckLoadingLayout(incoming);
        group = (CanvasGroup)Get(incoming, "group");
        Require(group.gameObject.activeInHierarchy && group.alpha > 0f, "Incoming loading panel did not preserve the layout during fade");
        yield return Scene(destination);
        Require(!group.gameObject.activeInHierarchy, "Loading panel blocks the destination UI");
        Pass(source + " -> " + destination + ": identical minimal loading layout on departure and arrival; panel releases UI");
    }

    private IEnumerator LoadingChecks()
    {
        var splash = Find<StartupController>();
        Require(splash != null && SceneTransition.StartupVisible, "No Boot splash");
        var branding = (CanvasGroup)Get(splash, "brandingGroup");
        var screen = (CanvasGroup)Get(splash, "screenGroup");
        Require(branding.alpha < 0.2f, "BP GAMES starts already opaque");
        var company = (TMPro.TMP_Text)Get(splash, "companyNameText");
        Require(company.text == "BP GAMES", "Wrong company name");
        yield return Capture("bp-games-black-frame");
        yield return Until(() => branding.alpha >= 0.15f, "visible BP GAMES fade");
        Require(branding.alpha < 0.95f && screen.alpha == 1f
            && SceneManager.GetActiveScene().name == "Boot", "Company fade skipped to full opacity / menu appeared early");
        yield return Capture("bp-games-fading-in");
        yield return Until(() => branding.alpha == 1f, "BP GAMES fully visible");
        Require(company.GetComponent<CanvasRenderer>().GetInheritedAlpha() > 0.99f && company.transform.lossyScale.x > 0f,
            "BP GAMES has a transparent parent or zero scale");
        yield return Capture("bp-games-visible");
        yield return Scene("MainMenu");
        Require(splash == null && !SceneTransition.StartupVisible, "Boot splash persists in MainMenu");
        Pass("Boot: BP GAMES renders transparent, partially visible and fully visible before MainMenu loads; fades then releases the menu");
        foreach (var level in LevelProgress.Levels)
        {
            LevelProgress.SelectLevel(level.Id);
            yield return CheckLoadingRoute(level.SceneName);
            yield return Click(Action("OpenPauseMenu"));
            yield return new WaitForSecondsRealtime(0.3f);
            // Проверяем реальную кнопку выхода: она открывает выбор уровней в MainMenu.
            var outgoing = Find<SceneTransition>();
            var group = (CanvasGroup)Get(outgoing, "group");
            yield return Click(Action("ReturnToMainMenu"));
            yield return Until(() => group.alpha == 1f && ((Image)Get(outgoing, "progressFill")).rectTransform.anchorMax.x >= 0.15f,
                "pause exit loading screen fully rendered");
            CheckLoadingLayout(outgoing);
            yield return Capture("loading-" + level.SceneName + "-exit-menu");
            yield return Until(() => SceneManager.GetActiveScene().name == "MainMenu", "pause exit activation");
            CheckLoadingLayout(Find<SceneTransition>());
            yield return Scene("MainMenu");
            Require(Find<MenuManager>().LevelsOpen, "Pause exit did not open level selection");
            Pass(level.SceneName + " -> MainMenu: pause exit retains the same loading layout and opens usable level selection");
        }
        Require(errors.Count == 0, "Loading routes produced runtime errors");
    }

    private IEnumerator PresentationChecks()
    {
        yield return CompactGameplayChecks();
    }


    private IEnumerator CompactGameplayChecks()
    {
        yield return Scene("MainMenu");
        if (UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Tilemap/Tilemap.prefab") != null)
        {
            yield return TilemapAndRollingChecks();
            yield break;
        }
        yield return Scene("MainMenu");
        foreach (var level in LevelProgress.Levels)
        {
            LevelProgress.SelectLevel(level.Id);
            SceneTransition.Load(level.SceneName);
            yield return Scene(level.SceneName);
            var slingshot = Find<SlingshotController>();
            var camera = Find<GameplayCameraController>();
            float firstSize = Camera.main.orthographicSize;
            yield return Until(() => !camera.IsBusy, "intro zoom");
            Require(Camera.main.orthographicSize < firstSize, "Intro did not zoom in");
            Vector3 fixedPosition = camera.transform.position;
            float fixedSize = Camera.main.orthographicSize;
            Find<LevelSession>().enabled = false;
            Require(All<Transform>().All(item => item.name != "GameplayCameraCanvas"), "Removed camera UI returned");
            foreach (var renderer in All<CatProjectile>().Select(cat => cat.GetComponent<SpriteRenderer>())
                .Concat(All<LevelTarget>().Select(target => target.GetComponent<SpriteRenderer>())).Where(item => item != null))
            {
                Vector3 low = Camera.main.WorldToViewportPoint(renderer.bounds.min);
                Vector3 high = Camera.main.WorldToViewportPoint(renderer.bounds.max);
                Require(low.x >= 0f && low.y >= 0f && high.x <= 1f && high.y <= 1f,
                    level.SceneName + ": object outside the fixed frame: " + renderer.name);
            }
            var loaded = (Rigidbody2D)Get(slingshot, "projectile");
            var waiting = (List<Rigidbody2D>)Get(slingshot, "waitingCats");
            Require(loaded.GetComponent<SpriteRenderer>().sprite == (Sprite)Get(loaded.GetComponent<CatProjectile>(), "launchSprite"),
                "Loaded cat does not use the new launch sprite");
            Require(loaded.GetComponent<CircleCollider2D>().bounds.extents.x < 1.5f, "Cat collider still uses old placeholder dimensions");
            for (int i = 0; i < waiting.Count; i++)
            {
                Bounds b = waiting[i].GetComponent<SpriteRenderer>().bounds;
                Require(Mathf.Abs(b.min.y - camera.ground.bounds.max.y) < 0.03f, "Queue cat does not stand on the ground");
                Require(Mathf.Abs(waiting[i].rotation) < 0.01f, "Queue cat is rotated");
                if (i > 0) Require(b.max.x < waiting[i - 1].GetComponent<SpriteRenderer>().bounds.min.x,
                    "Queue sprites overlap");
            }
            Camera.main.aspect = 4f / 3f;
            yield return null;
            foreach (var cat in All<CatProjectile>())
            {
                Vector3 viewport = Camera.main.WorldToViewportPoint(cat.transform.position);
                Require(viewport.x > 0f && viewport.x < 1f && viewport.y > 0f && viewport.y < 1f,
                    "Cat outside 4:3 frame");
            }
            Camera.main.aspect = 16f / 9f;
            yield return null;
            yield return CheckCameraButtons();
            yield return Capture(level.SceneName + "-fixed-map");
            Vector2 origin = new Vector2(960f, 540f);
            Set(slingshot, "dragStartScreen", origin);
            float fullPull = Camera.main.pixelHeight * slingshot.fullPullScreenFraction;
            Vector2 direction = new Vector2(Mathf.Cos(35f * Mathf.Deg2Rad), Mathf.Sin(35f * Mathf.Deg2Rad));
            var aim = typeof(SlingshotController).GetMethod("Aim", Flags);
            Quaternion rest = slingshot.rotatingPart.localRotation;
            aim.Invoke(slingshot, new object[] { origin - direction * fullPull });
            Require(Mathf.Abs(Mathf.DeltaAngle(rest.eulerAngles.z, slingshot.rotatingPart.localEulerAngles.z) - 35f) < 0.02f,
                "Catapult does not tilt from its saved pose");
            yield return null;
            Require(Vector3.Distance(loaded.transform.position, slingshot.launchPoint.position) < 0.01f,
                "Loaded cat sprite lags behind the rotating bowl");
            var dots = (List<SpriteRenderer>)Get(slingshot, "dots");
            var visible = dots.Where(dot => dot.gameObject.activeSelf).ToArray();
            Require(visible.Length > 3, "No dotted trajectory");
            foreach (var dot in visible)
            {
                var shadow = dot.transform.GetChild(0).GetComponent<SpriteRenderer>();
                Vector3 delta = Camera.main.WorldToScreenPoint(shadow.transform.position) - Camera.main.WorldToScreenPoint(dot.transform.position);
                Require(Mathf.Abs(delta.x + 2f) < 0.02f && Mathf.Abs(delta.y + 2f) < 0.02f
                    && shadow.sortingOrder < dot.sortingOrder && shadow.color.a > 0f,
                    "Trajectory shadow is not below-left: " + delta + " alpha " + shadow.color.a + " order " + shadow.sortingOrder + "/" + dot.sortingOrder);
                Require(dot.color.r > 0.6f && dot.color.g > 0.6f && dot.color.b > 0.5f, "Trajectory colors are too dark");
            }
            Vector2 velocity = (Vector2)Get(slingshot, "launchVelocity");
            Collider2D hit = null;
            bool structureHit = false;
            foreach (float referenceAngle in new[] { 35f, 30f, 25f, 40f, 45f, 20f, 15f })
            {
                direction = new Vector2(Mathf.Cos(referenceAngle * Mathf.Deg2Rad), Mathf.Sin(referenceAngle * Mathf.Deg2Rad));
                aim.Invoke(slingshot, new object[] { origin - direction * fullPull });
                velocity = (Vector2)Get(slingshot, "launchVelocity");
                Physics2D.SyncTransforms();
                structureHit = slingshot.TryPredictImpact(loaded.transform.position, velocity, loaded.gravityScale,
                    out _, out _, out hit) && (hit.GetComponentInParent<LevelTarget>() != null || hit.GetComponentInParent<MouseEnemy>() != null);
                if (structureHit) break;
            }
            Require(structureHit, level.SceneName + ": no reference shot reaches the structure");
            // Дальность до последней мыши без увеличения Launch Power.
            float rightmost = All<MouseEnemy>().Max(enemy => enemy.transform.position.x);
            float reach = float.NegativeInfinity;
            for (float angle = 20f; angle <= 65f; angle += 5f)
            {
                Vector2 v = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad))
                    * slingshot.launchPower * slingshot.maxPullDistance;
                Vector2 pullDirection = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                aim.Invoke(slingshot, new object[] { origin - pullDirection * fullPull });
                Vector2 point = slingshot.launchPoint.position;
                for (int i = 0; i < 500 && point.y > camera.ground.bounds.max.y; i++)
                {
                    v = (v + Physics2D.gravity * loaded.gravityScale * Time.fixedDeltaTime)
                        / (1f + slingshot.airResistance * Time.fixedDeltaTime);
                    point += v * Time.fixedDeltaTime;
                }
                reach = Mathf.Max(reach, point.x);
            }
            Require(reach >= rightmost + 1f, "Maximum range cannot reach the last mouse: " + reach + "/" + rightmost);
            aim.Invoke(slingshot, new object[] { origin - direction * fullPull });
            if (level.Id == 1) yield return Capture("pastel-trajectory");
            aim.Invoke(slingshot, new object[] { origin + Vector2.right * fullPull });
            Require(((Vector2)Get(slingshot, "launchVelocity")).sqrMagnitude == 0f
                && Quaternion.Angle(rest, slingshot.rotatingPart.localRotation) < 0.01f,
                "Backward drag turns the catapult / launches backwards");
            aim.Invoke(slingshot, new object[] { origin - Vector2.down * fullPull - Vector2.right * 0.1f });
            Require(Quaternion.Angle(rest, slingshot.rotatingPart.localRotation) <= slingshot.maxArmTilt + 0.01f,
                "Catapult exceeds its tilt limit");
            slingshot.CancelInput();
            Require(Quaternion.Angle(rest, slingshot.rotatingPart.localRotation) < 0.01f
                && Mathf.Abs(loaded.rotation) < 0.01f, "Cancel did not restore catapult and cat");
            aim.Invoke(slingshot, new object[] { origin - direction * fullPull });
            int impacts = 0;
            slingshot.ProjectileImpacted += point => impacts++;
            slingshot.StartCoroutine((IEnumerator)typeof(SlingshotController).GetMethod("LaunchRoutine", Flags).Invoke(slingshot, null));
            yield return Until(() => slingshot.ShotsFired == 1, "shot launched");
            while (slingshot.IsShotInProgress)
            {
                Require(camera.transform.position == fixedPosition && Camera.main.orthographicSize == fixedSize,
                    "Camera moved during flight");
                yield return null;
            }
            Require(impacts == 1 && slingshot.WaitingCatCount == Mathf.Min(level.Id + 1, 5) - 2,
                "Impact / queue count incorrect");
            Require(Quaternion.Angle(rest, slingshot.rotatingPart.localRotation) < 0.01f
                && Mathf.Abs(((Rigidbody2D)Get(slingshot, "projectile")).rotation) < 0.01f,
                "Next cat is rotated or arm did not reset");
            if (level.Id == 1) yield return MouseContactChecks(camera);
            int ammo = Mathf.Min(level.Id + 1, 5);
            for (int shot = 2; shot <= ammo; shot++)
            {
                var fired = (Rigidbody2D)Get(slingshot, "projectile");
                Sprite sprite = fired.GetComponent<SpriteRenderer>().sprite;
                Rigidbody2D next = waiting.Count > 0 ? waiting[0] : null;
                aim.Invoke(slingshot, new object[] { origin - direction * fullPull });
                slingshot.StartCoroutine((IEnumerator)typeof(SlingshotController).GetMethod("LaunchRoutine", Flags).Invoke(slingshot, null));
                Require(fired.simulated && fired.GetComponent<SpriteRenderer>().sprite == sprite
                    && (Rigidbody2D)Get(slingshot, "projectile") == next, "Cat identity / texture lost on launch");
                if (next != null && shot == 2)
                {
                    yield return Click(Action("OpenPauseMenu"));
                    Vector3 frozen = next.transform.position;
                    yield return new WaitForSecondsRealtime(0.2f);
                    Require(next.transform.position == frozen, "Queue moved during pause");
                    yield return Click(Action("ResumeGame"));
                }
                yield return Until(() => !slingshot.IsShotInProgress, "remaining queue shot", 20f);
                Require(slingshot.ShotsFired == shot && slingshot.WaitingCatCount == Mathf.Max(0, ammo - shot - 1)
                    && camera.transform.position == fixedPosition && Camera.main.orthographicSize == fixedSize,
                    "Queue count / fixed camera changed");
            }
            Require(!slingshot.CanAim && (Rigidbody2D)Get(slingshot, "projectile") == null, "Extra cat remains after queue exhaustion");
            Pass(level.SceneName + ": intro, map fit, actual atlas cats, queue, limited arm, shadows, predicted/physical hit, fixed flight camera");
            var oldSession = Find<LevelSession>();
            oldSession.RestartLevel();
            yield return Until(() => oldSession == null, "restart unload");
            yield return Scene(level.SceneName);
            var restartedCamera = Find<GameplayCameraController>();
            Find<PauseMenuController>().OpenPauseMenu();
            float pausedIntro = Camera.main.orthographicSize;
            yield return new WaitForSecondsRealtime(0.2f);
            Require(Camera.main.orthographicSize == pausedIntro, "Intro zoom moved during pause");
            Find<PauseMenuController>().ResumeGame();
            Require(Find<SlingshotController>().ShotsFired == 0
                && Find<SlingshotController>().WaitingCatCount == Mathf.Min(level.Id + 1, 5) - 1,
                "Restart did not restore the queue");
            yield return Until(() => !Find<GameplayCameraController>().IsBusy, "restart intro");
        }
        Require(errors.Count == 0, "Compact gameplay has runtime errors");
    }

    private IEnumerator MouseContactChecks(GameplayCameraController camera)
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/MouseEnemy.prefab");
        var falling = Instantiate(prefab, new Vector3(0f, 0f, 0f), Quaternion.identity).GetComponent<MouseEnemy>();
        int deaths = 0;
        falling.Died += enemy => deaths++;
        float radius = falling.GetComponent<Collider2D>().bounds.extents.y;
        falling.transform.position = new Vector3(0f, camera.ground.bounds.max.y + radius + 0.03f, 0f);
        yield return Until(() => falling.IsDead, "gentle floor contact kills mouse", 3f);
        falling.TakeDamage(1000f);
        Require(falling.IsDeathAnimating && !falling.GetComponent<Rigidbody2D>().simulated
            && !falling.GetComponent<Collider2D>().enabled, "Floor contact did not disable physics immediately");
        yield return new WaitForSeconds(0.12f);
        Require(falling.GetComponent<SpriteRenderer>().sprite == (Sprite)Get(falling, "deathSprite"),
            "Mouse death pose not applied");
        yield return Capture("mouse-death-flight");
        yield return Click(Action("OpenPauseMenu"));
        Vector3 deathPosition = falling.transform.position;
        Color deathColor = falling.GetComponent<SpriteRenderer>().color;
        yield return new WaitForSecondsRealtime(0.2f);
        Require(falling.transform.position == deathPosition && falling.GetComponent<SpriteRenderer>().color == deathColor,
            "Mouse death animation moved while paused");
        yield return Click(Action("ResumeGame"));
        yield return Until(() => !falling.IsDeathAnimating, "mouse death animation completes", 3f);
        Require(deaths == 1 && !falling.GetComponent<Rigidbody2D>().simulated
            && !falling.GetComponent<SpriteRenderer>().enabled, "Floor death duplicated or mouse keeps rolling");
        var support = new GameObject("MouseTestSupport");
        support.transform.position = new Vector3(0f, 9f, 0f);
        support.AddComponent<BoxCollider2D>().size = new Vector2(5f, 1f);
        var target = Instantiate(prefab, new Vector3(0f, 9.5f + radius + 0.02f, 0f), Quaternion.identity).GetComponent<MouseEnemy>();
        yield return new WaitForSeconds(0.5f);
        Require(!target.IsDead, "Mouse died from contact with its platform");
        int damageEvents = 0;
        int killEvents = 0;
        target.Damaged += (enemy, damage) => damageEvents++;
        target.Died += enemy => killEvents++;
        var catPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<Rigidbody2D>("Assets/Prefabs/Cats/Cat1.prefab");
        var cat = Instantiate(catPrefab, target.transform.position + Vector3.left * 3f + Vector3.up * 0.6f, Quaternion.identity);
        cat.gravityScale = 0f;
        cat.linearVelocity = Vector2.right;
        int reportedImpacts = 0;
        cat.GetComponent<ProjectileImpactReporter>().Arm(point => reportedImpacts++);
        yield return Until(() => target.IsDead, "slow projectile contact kills mouse", 4f);
        target.TakeDamage(1000f);
        Require(target.Health == 0f && killEvents == 1 && damageEvents == 1 && reportedImpacts == 1,
            "Direct hit did not kill once / award one damage event");
        Destroy(cat.gameObject); Destroy(target.gameObject); Destroy(falling.gameObject); Destroy(support);
        Pass("Mouse: gentle floor touch kills, platforms do not, slow cat hit kills once, instant physics stop, death pose/hop/fade, pause and cleanup");
    }

    private IEnumerator TilemapAndRollingChecks()
    {
        yield return Scene("MainMenu");
        foreach (string name in new[] { "Gameplay", "Level2" })
        {
            LevelProgress.SelectLevel(name == "Gameplay" ? 1 : 2);
            SceneTransition.Load(name);
            yield return Scene(name);
            var camera = Find<GameplayCameraController>();
            yield return Until(() => !camera.IsBusy, "intro");
            var session = Find<LevelSession>();
            session.enabled = false;
            var slingshot = Find<SlingshotController>();
            Require(All<Transform>().All(item => item.name != "FlatGround"), "Deleted floor remains");
            Require(camera.TerrainCollider is UnityEngine.Tilemaps.TilemapCollider2D
                && camera.TerrainCollider.name == "Layer1"
                && camera.TerrainCollider.attachedRigidbody.bodyType == RigidbodyType2D.Static,
                "Wrong terrain collider / moving terrain");
            var waiting = (List<Rigidbody2D>)Get(slingshot, "waitingCats");
            var slots = (Transform[])Get(slingshot, "queueSlots");
            Require(slots.Length == 2 && waiting.Count == session.MaxProjectiles - 1, "Wrong compact queue");
            for (int i = 0; i < waiting.Count; i++)
            {
                Bounds bounds = waiting[i].GetComponent<SpriteRenderer>().bounds;
                Require(camera.TryGetSurfaceHeight(waiting[i].position.x, out float surface)
                    && bounds.min.y >= surface && bounds.min.y - surface < 0.08f,
                    "Queued cat is below / above tilemap surface");
                Require(Mathf.Abs(waiting[i].position.x - slots[i].position.x) < 0.01f,
                    "Queue ignores its individual positions");
                Require(!waiting[i].simulated, "Queued cat participates in physics");
            }
            if (waiting.Count == 2)
                Require(waiting[0].position.x < slingshot.pivotPoint.position.x
                    && waiting[1].position.x > slingshot.pivotPoint.position.x,
                    "Cats are not on both sides of the catapult");
            yield return Capture(name + "-tilemap-queue");
            yield return TilemapMouseChecks(camera, slots[0].position.x);
            if (name == "Gameplay")
                yield return PersistentCollisionChecks(camera, slingshot, slots[0].position.x);

            int ammo = session.MaxProjectiles;
            slingshot.spawnDelay = 0.15f;
            Vector2 origin = new Vector2(960f, 540f);
            float pullPixels = Camera.main.pixelHeight * slingshot.fullPullScreenFraction;
            var aim = typeof(SlingshotController).GetMethod("Aim", Flags);
            for (int shot = 1; shot <= ammo; shot++)
            {
                var fired = (Rigidbody2D)Get(slingshot, "projectile");
                var next = waiting.Count > 0 ? waiting[0] : null;
                Set(slingshot, "dragStartScreen", origin);
                aim.Invoke(slingshot, new object[] { origin - new Vector2(0.87f, 0.5f) * pullPixels });
                slingshot.StartCoroutine((IEnumerator)typeof(SlingshotController).GetMethod("LaunchRoutine", Flags).Invoke(slingshot, null));
                Require((Rigidbody2D)Get(slingshot, "projectile") == next, "Loading replaced the next cat");
                if (next != null)
                {
                    yield return Click(Action("OpenPauseMenu"));
                    Vector3 frozen = next.transform.position;
                    yield return new WaitForSecondsRealtime(0.15f);
                    Require(next.transform.position == frozen, "Loading moves on pause");
                    yield return Click(Action("ResumeGame"));
                }
                yield return Until(() => !slingshot.IsShotInProgress, "queue shot", 20f);
                Require(slingshot.ShotsFired == shot && slingshot.WaitingCatCount == Mathf.Max(0, ammo - shot - 1),
                    "Compact queue does not advance / deplete");
                if (waiting.Count > 0)
                {
                    var cat = waiting[0];
                    Require(camera.TryGetSurfaceHeight(cat.position.x, out float surface)
                        && cat.GetComponent<SpriteRenderer>().bounds.min.y >= surface,
                        "Advanced queue went underground");
                }
                if (fired != null && !fired.GetComponent<CatFlightEffects>().IsExiting)
                    Require(fired.simulated, "Shot physics disabled after collision");
                if (shot == ammo)
                {
                    var objective = new GameObject("UnclearedDefeatCheck");
                    objective.transform.position = new Vector3(80f, 40f, 0f);
                    objective.AddComponent<BoxCollider2D>();
                    var body = objective.AddComponent<Rigidbody2D>();
                    body.bodyType = RigidbodyType2D.Static;
                    var target = objective.AddComponent<LevelTarget>();
                    Set(session, "targets", ((LevelTarget[])Get(session, "targets")).Concat(new[] { target }).ToArray());
                    Set(session, "targetBodies", ((Rigidbody2D[])Get(session, "targetBodies")).Concat(new[] { body }).ToArray());
                    session.enabled = true;
                    if (fired != null && fired.GetComponent<CatFlightEffects>().HasImpacted && fired.simulated)
                    {
                        fired.linearVelocity = Vector2.zero;
                        fired.angularVelocity = 0f;
                        fired.Sleep();
                    }
                    float deadline = Time.realtimeSinceStartup + 15f;
                    while (slingshot.HasMovingProjectiles)
                    {
                        Require(Time.timeScale > 0f && !(bool)Get(session, "failed"),
                            "Defeat froze a cat before its physical lifetime / fade completed");
                        Require(Time.realtimeSinceStartup < deadline, "Cat cleanup never finishes");
                        yield return null;
                    }
                    yield return Until(() => (bool)Get(session, "failed"), "defeat after cleanup", 5f);
                    Destroy(objective);
                    Pass(name + ": last shot can settle and fade before defeat freezes the level");
                }
            }
            yield return Until(() => !slingshot.HasMovingProjectiles, "all cats finish physical life and fade", 15f);
            Require(All<ParticleSystem>().All(item => item.name != "FurTrail(Clone)"), "Cat fur leaked");
            session.enabled = true;
            yield return Until(() => session.IsEnding, "level resolves after shots", 5f);
            var oldSession = session;
            session.RestartLevel();
            yield return Until(() => oldSession == null, "restart unload");
            yield return Scene(name);
            Require(Find<SlingshotController>().ShotsFired == 0
                && Find<SlingshotController>().WaitingCatCount == ammo - 1, "Restart lost the compact queue");
            Pass(name + ": Layer1 terrain, floor removed, cats above tiles and around catapult, all shots, loading pause, cleanup and restart");
        }
        // Прежние уровни продолжают использовать свою землю.
        foreach (var level in LevelProgress.Levels.Where(item => item.Id >= 3))
        {
            LevelProgress.SelectLevel(level.Id);
            SceneTransition.Load(level.SceneName);
            yield return Scene(level.SceneName);
            yield return Until(() => !Find<GameplayCameraController>().IsBusy, "legacy intro");
            var camera = Find<GameplayCameraController>();
            Require(camera.TerrainCollider != null && camera.ground != null, "Legacy ground fallback lost");
            foreach (var cat in All<CatProjectile>().Where(item => item.IsQueued))
                Require(camera.TryGetSurfaceHeight(cat.transform.position.x, out float y)
                    && cat.GetComponent<SpriteRenderer>().bounds.min.y >= y, "Legacy queue below its floor");
            Pass(level.SceneName + ": legacy ground and queue preserved");
        }
    }

    private IEnumerator TilemapMouseChecks(GameplayCameraController camera, float x)
    {
        Require(camera.TryGetSurfaceHeight(x, out float y), "No tile surface for mouse check");
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/MouseEnemy.prefab");
        var mouse = Instantiate(prefab, new Vector3(x, y + 2f, 0f), Quaternion.identity).GetComponent<MouseEnemy>();
        Bounds bounds = mouse.GetComponent<Collider2D>().bounds;
        mouse.transform.position += Vector3.up * (y + 0.05f - bounds.min.y);
        int deaths = 0;
        mouse.Died += item => deaths++;
        yield return Until(() => mouse.IsDead, "mouse touches Layer1", 3f);
        Require(deaths == 1 && mouse.IsDeathAnimating && !mouse.GetComponent<Rigidbody2D>().simulated,
            "Tile contact does not immediately kill / animate mouse");
        yield return Until(() => !mouse.IsDeathAnimating, "mouse death completes", 3f);
        Destroy(mouse.gameObject);
        Pass("Mouse dies once on dense Layer1 contact, plays its death animation and disables physics immediately");
    }

    private IEnumerator PersistentCollisionChecks(GameplayCameraController camera, SlingshotController slingshot, float x)
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<Rigidbody2D>("Assets/Prefabs/Cats/Cat1.prefab");
        // Первая преграда, затем отдельная коробка и мышь: один кот должен сохранить урон.
        var cat = Instantiate(prefab, new Vector3(-10f, 26f, 0f), Quaternion.identity);
        cat.GetComponent<CatProjectile>().ShowLoaded();
        cat.gravityScale = 0f;
        cat.linearVelocity = Vector2.right * 15f;
        var effects = cat.GetComponent<CatFlightEffects>();
        int impacts = 0;
        cat.GetComponent<ProjectileImpactReporter>().Arm(point => impacts++);
        cat.GetComponent<ProjectileComicEffects>().BeginFlight();
        effects.BeginFlight(camera.mapMin.y);
        var obstacle = new GameObject("FirstCollisionCheck");
        obstacle.transform.position = new Vector3(-6f, 26f, 0f);
        obstacle.AddComponent<BoxCollider2D>();
        yield return Until(() => effects.HasImpacted, "first physical contact", 3f);
        Require(cat.simulated && cat.GetComponent<Collider2D>().enabled && !effects.IsExiting,
            "Cat became decorative after its first collision");
        Destroy(obstacle);
        yield return null;
        var box = new GameObject("SecondCollisionCheck");
        box.transform.position = cat.transform.position + Vector3.right * 4f;
        box.AddComponent<BoxCollider2D>();
        box.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        var target = box.AddComponent<LevelTarget>();
        Set(target, "countsForVictory", false);
        cat.linearVelocity = Vector2.right * 15f;
        yield return Until(() => target.IsCleared, "second collision damages a target", 3f);
        Destroy(box);
        yield return null;
        var mousePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/MouseEnemy.prefab");
        var mouse = Instantiate(mousePrefab, cat.transform.position + Vector3.right * 3f, Quaternion.identity).GetComponent<MouseEnemy>();
        mouse.GetComponent<Rigidbody2D>().gravityScale = 0f;
        cat.linearVelocity = Vector2.right * 12f;
        yield return Until(() => mouse.IsDead, "later collision kills a mouse", 3f);
        Require(impacts == 1 && effects.HasImpacted && cat.simulated,
            "First impact was duplicated / later damage disabled");
        Destroy(cat.gameObject); Destroy(mouse.gameObject);
        yield return null;

        Require(camera.TryGetSurfaceHeight(x, out float y), "No rolling surface");
        cat = Instantiate(prefab, new Vector3(x, y + 2f, 0f), Quaternion.identity);
        cat.GetComponent<CatProjectile>().ShowLoaded();
        cat.gravityScale = 1f;
        cat.linearVelocity = new Vector2(7f, -2f);
        effects = cat.GetComponent<CatFlightEffects>();
        cat.GetComponent<ProjectileComicEffects>().BeginFlight();
        effects.BeginFlight(camera.mapMin.y);
        yield return Until(() => effects.HasImpacted, "cat lands on Tilemap", 3f);
        float impactedAt = Time.time;
        float hitX = cat.position.x;
        yield return new WaitForSeconds(0.35f);
        Require(cat.simulated && cat.GetComponent<Collider2D>().enabled && cat.position.x > hitX + 0.25f,
            "Cat cannot roll along the ground after impact");
        var fur = (ParticleSystem)Get(effects, "fur");
        Require(fur != null && !fur.isEmitting, "Fur still emits after landing");
        yield return Capture("cat-rolling-on-layer1");
        yield return Click(Action("OpenPauseMenu"));
        Vector3 paused = cat.transform.position;
        yield return new WaitForSecondsRealtime(0.2f);
        Require(cat.transform.position == paused && cat.simulated, "Rolling moved on pause / physics removed");
        yield return Click(Action("ResumeGame"));
        cat.linearVelocity = Vector2.zero;
        cat.angularVelocity = 0f;
        cat.Sleep();
        Require(effects.HasPendingCleanup, "Stopped cat does not delay defeat");
        yield return Until(() => effects.IsExiting, "settled cat starts fading", 10f);
        Require(Time.time - impactedAt >= 2.95f && !cat.simulated, "Cat disappears too soon after impact");
        yield return Until(() => cat == null, "cat fades and disappears", 3f);
        Require(All<ParticleSystem>().All(item => item.name != "FurTrail(Clone)"), "Rolling cat leaked fur");
        Pass("Cat keeps physics after first impact, damages a second box and a mouse, rolls, pauses, settles for minimum lifetime and fades cleanly");
    }

    private IEnumerator Checks()
    {
        if (Environment.GetCommandLineArgs().Contains("-bpgTilemapAndRollingOnly"))
        {
            yield return TilemapAndRollingChecks();
            yield break;
        }
        if (Environment.GetCommandLineArgs().Contains("-bpgCompactGameplayOnly"))
        {
            yield return CompactGameplayChecks();
            yield break;
        }
        if (Environment.GetCommandLineArgs().Contains("-bpgLoadingOnly"))
        {
            yield return LoadingChecks();
            yield break;
        }
        if (Environment.GetCommandLineArgs().Contains("-bpgPresentationOnly"))
        {
            yield return PresentationChecks();
            if (Environment.GetCommandLineArgs().Contains("-bpgCatFlightOnly"))
            {
                SceneTransition.Load("MainMenu");
                yield return CatFlightChecks();
            }
            yield break;
        }
        if (Environment.GetCommandLineArgs().Contains("-bpgCatFlightOnly"))
        {
            yield return CatFlightChecks();
            yield break;
        }
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
        yield return Until(() => !camera.IsBusy, "intro completed");
        Vector3 fixedPosition = camera.transform.position;
        float fixedSize = Camera.main.orthographicSize;
        yield return new WaitForSeconds(0.2f);
        Require(camera.transform.position == fixedPosition && Camera.main.orthographicSize == fixedSize,
            "Camera moved after intro");

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
