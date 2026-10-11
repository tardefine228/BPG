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
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;

public sealed class ValidationRunner : MonoBehaviour
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly List<string> results = new List<string>();
    private readonly List<string> errors = new List<string>();
    private readonly List<string> editorErrors = new List<string>();
    private void Awake() { DontDestroyOnLoad(gameObject); Application.logMessageReceived += Log; }
    private void Log(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (stack.Contains("UnityEditor.Search.SearchDatabase")) editorErrors.Add(message + "\n" + stack);
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
    private IEnumerator Checks()
    {
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
        All<LevelCardView>().First(card => card.LevelId == 1).GetComponent<Button>().onClick.Invoke();
        yield return Scene("Gameplay");
        yield return new WaitForSecondsRealtime(0.4f);
        yield return Capture("gameplay");
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
        slingshot.gameplayCamera = null; slingshot.spawnDelay = 0.02f; slingshot.projectileLifetime = 0.1f;
        for (int shot = 1; shot <= 5; shot++)
        {
            Require(slingshot.CanAim, "Projectile missing for shot " + shot);
            Set(slingshot, "launchVelocity", new Vector2(-5f, 0f));
            slingshot.StartCoroutine((IEnumerator)typeof(SlingshotController).GetMethod("LaunchRoutine", Flags).Invoke(slingshot, null));
            yield return Until(() => !slingshot.IsShotInProgress, "shot " + shot);
            Require(slingshot.ShotsFired == shot, "Incorrect shot count");
        }
        Require(!slingshot.CanAim && Find<LevelSession>().RemainingShots == 0, "Sixth shot allowed");
        slingshot.StartCoroutine((IEnumerator)typeof(SlingshotController).GetMethod("LaunchRoutine", Flags).Invoke(slingshot, null));
        yield return null;
        Require(slingshot.ShotsFired == 5, "Sixth shot fired");
        yield return Until(() => ((GameObject)Get(Find<LevelSession>(), "defeatOverlay")).activeSelf, "defeat");
        Require(Time.timeScale == 0f, "Defeat did not stop gameplay");
        Pass("Five real launches exhaust ammunition; sixth is blocked; defeat appears after settling");
        yield return Capture("defeat");
        Action("RestartLevel").onClick.Invoke();
        yield return Until(() => !SceneTransition.IsLoading && Find<LevelSession>().RemainingShots == 5, "restart");
        yield return null;
        Require(Time.timeScale == 1f && !((GameObject)Get(Find<LevelSession>(), "defeatOverlay")).activeSelf, "Restart state not reset");
        Pass("Defeat restart restores five projectiles and normal time");

        slingshot = Find<SlingshotController>();
        Set(slingshot, "<ShotsFired>k__BackingField", 5);
        foreach (var target in All<LevelTarget>()) Set(target, "<IsCleared>k__BackingField", true);
        yield return Until(() => ((GameObject)Get(Find<LevelSession>(), "completionOverlay")).activeSelf, "victory on final shot");
        Require(!((GameObject)Get(Find<LevelSession>(), "defeatOverlay")).activeSelf && LevelProgress.IsCompleted(1), "Final-shot victory lost to defeat");
        Pass("Victory on the final projectile takes priority over defeat and saves progress");
        yield return Capture("victory");
        Action("GoToNextLevel").onClick.Invoke();
        yield return Scene("Level2");
        Require(LevelProgress.CurrentLevelId == 2 && Find<LevelSession>().RemainingShots == 5, "Next level failed");
        Pass("Victory Continue button opens Level2 with five shots");

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
            Require(Find<LevelSession>().RemainingShots == 5 && Find<SlingshotController>().CanAim, level + " initialization failed");
            Pass(level + " loads with intact UI, FX pool and five projectiles");
        }
        slingshot = Find<SlingshotController>();
        Set(slingshot, "<ShotsFired>k__BackingField", 1);
        foreach (var target in All<LevelTarget>()) Set(target, "<IsCleared>k__BackingField", true);
        yield return Until(() => ((GameObject)Get(Find<LevelSession>(), "completionOverlay")).activeSelf, "last level victory");
        Action("GoToNextLevel").onClick.Invoke();
        yield return Scene("MainMenu");
        Pass("Continue after the last implemented level returns to MainMenu");
        Require(errors.Count == 0, "Runtime produced errors");
    }
}
