using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Проверяет сохранённые связи интерфейса без запуска игры и возвращает открытые сцены.
public static class SceneUiValidation
{
    [MenuItem("BPG/Проверить интерфейс всех сцен")]
    public static void Validate()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (var entry in EditorBuildSettings.scenes.Where(scene => scene.enabled))
            {
                var scene = EditorSceneManager.OpenScene(entry.path);
                foreach (var root in scene.GetRootGameObjects())
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0,
                        entry.path + ": отсутствует скрипт на " + child.name);

                foreach (var button in Find<Button>())
                {
                    Require(button.onClick.GetPersistentEventCount() > 0, button.name + ": не назначен On Click");
                    for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    {
                        Object target = button.onClick.GetPersistentTarget(i);
                        string method = button.onClick.GetPersistentMethodName(i);
                        Require(target != null && target.GetType().GetMethods().Any(info => info.Name == method),
                            button.name + ": потерян обработчик " + method);
                    }
                }

                References(Find<SceneTransition>().Single(), "settings", "group", "progressFill", "caption", "percent",
                    "logoImage", "titleText", "subtitleText", "backgroundImage", "accentLine");
                foreach (var audio in Find<AudioManager>()) References(audio, "musicSource", "effectsSource", "uiSource");
                foreach (var session in Find<LevelSession>())
                {
                    Require(new SerializedObject(session).FindProperty("maxProjectiles").intValue >= 1,
                        scene.name + ": боезапас должен быть положительным");
                    References(session, "gameplayController", "starOnSprite", "starOffSprite", "scoreValueText",
                        "scoreHintText", "completionOverlay", "defeatOverlay",
                        "completionScoreText", "completionShotsText", "completionStars", "victorySource");
                    References(Find<PauseMenuController>().Single(), "pausePage", "settingsPage", "pauseButton",
                        "musicVolumeSlider", "effectsVolumeSlider", "overlayFade", "pauseFade", "settingsFade");
                    ValidatePauseHierarchy(Find<PauseMenuController>().Single());
                    References(Find<GameplayCameraController>().Single(), "controlsCanvas", "controlsRect",
                        "launcherButton", "overviewButton", "targetButton");
                    References(Find<SlingshotController>().Single(), "dots");
                    ValidateMice(scene.name, Find<GameplayCameraController>().Single());
                    ValidateBlocks(scene.name, Find<GameplayCameraController>().Single());
                    var slingshot = Find<SlingshotController>().Single();
                    References(slingshot, "catProjectilePrefabs", "queueOrigin");
                    var cats = new SerializedObject(slingshot).FindProperty("catProjectilePrefabs");
                    Require(cats.arraySize == 5, scene.name + ": должно быть пять видов котов");
                    Require(Enumerable.Range(0, cats.arraySize).Select(index =>
                        cats.GetArrayElementAtIndex(index).objectReferenceValue).Distinct().Count() == 5,
                        scene.name + ": префабы котов должны быть разными");
                    int expectedAmmo = scene.name == "Gameplay" ? 2 : scene.name == "Level2" ? 3 :
                        scene.name == "Level3" ? 4 : 5;
                    Require(session.MaxProjectiles == expectedAmmo, scene.name + ": неверное количество котов");
                    var dots = new SerializedObject(Find<SlingshotController>().Single()).FindProperty("dots");
                    Require(dots.arraySize > 0, "Не сохранены точки траектории");
                    Require(Find<ComicParticleBurst>().Length > 0, "Не сохранены эффекты попадания");
                    References(Find<ComicEffectsPool>().Single(), "bursts");
                    foreach (var burst in Find<ComicParticleBurst>()) References(burst, "dust", "chips", "flash");
                    foreach (var particles in Find<ParticleSystem>())
                        Require(particles.main.simulationSpace == ParticleSystemSimulationSpace.World,
                            particles.name + ": неверное пространство частиц");
                }
                foreach (var select in Find<LevelSelectController>())
                    References(select, "cards", "track", "pagingSurface", "starsSummary", "pageDots", "menuManager");
                foreach (var card in Find<LevelCardView>())
                {
                    References(card, "button", "preview", "hoverHighlight", "lockOverlay", "soonOverlay", "parHint", "stars");
                    var highlight = (GameObject)new SerializedObject(card).FindProperty("hoverHighlight").objectReferenceValue;
                    if (highlight.transform.parent == card.transform.parent)
                        Require(highlight.transform.GetSiblingIndex() < card.transform.GetSiblingIndex(),
                            card.name + ": подсветка перекрывает карточку");
                    Require(!highlight.activeSelf, card.name + ": обводка должна появляться только при наведении");
                }
                Debug.Log("UI CHECK OK: " + scene.name + ", кнопок: " + Find<Button>().Length);
            }
            var projectile = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Projectile.prefab");
            Require(projectile.GetComponent<ProjectileImpactReporter>() != null, "У снаряда нет обработчика попаданий");
            References(projectile.GetComponent<ProjectileComicEffects>(), "comicMaterial", "trailRoot", "outline", "ribbon", "core");
            for (int i = 1; i <= 5; i++)
            {
                var cat = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Cats/Cat" + i + ".prefab");
                Require(cat != null && cat.GetComponent<CatProjectile>() != null
                    && cat.GetComponent<Rigidbody2D>() != null && cat.GetComponent<CircleCollider2D>() != null
                    && cat.GetComponent<ProjectileImpactReporter>() != null, "Cat" + i + ": неполный префаб снаряда");
                References(cat.GetComponent<CatProjectile>(), "spriteRenderer");
                References(cat.GetComponent<CatFlightEffects>(), "furPrefab");
                References(cat.GetComponent<ProjectileComicEffects>(), "comicMaterial", "trailRoot", "outline", "ribbon", "core");
            }
            var fur = AssetDatabase.LoadAssetAtPath<ParticleSystem>("Assets/Prefabs/Cats/FurTrail.prefab");
            Require(fur != null && !fur.main.playOnAwake
                && fur.main.simulationSpace == ParticleSystemSimulationSpace.World,
                "Шерсть: неверные настройки префаба частиц");
            References(fur.GetComponent<ParticleSystemRenderer>(), "m_Materials");
            Debug.Log("Все связи интерфейса и эффектов проверены.");
        }
        finally
        {
            if (setup.Any(scene => scene.isActive && scene.isLoaded))
                EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    private static T[] Find<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include);

    // Мыши — цели уровня: стоят на постройке внутри TargetArea, а земля помечена тегом Ground.
    private static void ValidateMice(string sceneName, GameplayCameraController camera)
    {
        var mice = Find<MouseTarget>();
        Require(mice.Length > 0, sceneName + ": на уровне нет мышей");
        Require(camera.ground != null && camera.ground.CompareTag("Ground"),
            sceneName + ": у FlatGround должен быть тег Ground");
        float groundTop = camera.ground.bounds.max.y;
        foreach (var mouse in mice)
        {
            Require(camera.targetFocus != null && mouse.transform.IsChildOf(camera.targetFocus),
                sceneName + ": " + mouse.name + " должна находиться внутри TargetArea");
            var body = mouse.GetComponent<Collider2D>();
            Require(body != null && body.bounds.min.y > groundTop + 0.5f,
                sceneName + ": " + mouse.name + " не должна стоять на земле");
        }
    }

    // Плиточные блоки построек: коллайдер равен видимому размеру, блок не утоплен в землю.
    // С Auto Tiling Unity сама пересчитывает размер коллайдера, и блоки проваливались сквозь землю.
    private static void ValidateBlocks(string sceneName, GameplayCameraController camera)
    {
        float groundTop = camera.ground.bounds.max.y;
        foreach (var block in Find<LevelTarget>())
        {
            var sprite = block.GetComponent<SpriteRenderer>();
            var box = block.GetComponent<BoxCollider2D>();
            if (sprite == null || box == null || sprite.drawMode == SpriteDrawMode.Simple)
                continue;
            string label = sceneName + ": " + block.name;
            Require(!box.autoTiling, label + " — у BoxCollider2D должен быть выключен Auto Tiling");
            Require(Vector2.Distance(box.size, sprite.size) < 0.001f,
                label + " — Size у BoxCollider2D должен совпадать с Size у SpriteRenderer");
            Require(box.bounds.min.y > groundTop - 0.05f, label + " — блок утоплен в землю");
        }
    }

    private static void ValidatePauseHierarchy(PauseMenuController pause)
    {
        var serialized = new SerializedObject(pause);
        var button = (Button)serialized.FindProperty("pauseButton").objectReferenceValue;
        var overlay = (PanelFade)serialized.FindProperty("overlayFade").objectReferenceValue;
        var canvas = button.GetComponentInParent<Canvas>(true);
        Require(canvas != null && canvas.isActiveAndEnabled,
            "Пауза: кнопка должна находиться внутри включённого Canvas");
        Require(overlay.GetComponentInParent<Canvas>(true) == canvas,
            "Пауза: Overlay должен находиться внутри того же Canvas, что и кнопка");
        foreach (string field in new[] { "pausePage", "settingsPage" })
        {
            var page = (GameObject)serialized.FindProperty(field).objectReferenceValue;
            Require(page.transform.IsChildOf(overlay.transform),
                "Пауза: " + field + " должен находиться внутри Overlay");
        }
    }

    private static void References(Object owner, params string[] fields)
    {
        Require(owner != null, "Отсутствует обязательный компонент");
        var serialized = new SerializedObject(owner);
        foreach (string field in fields)
        {
            var property = serialized.FindProperty(field);
            Require(property != null, owner.name + ": отсутствует поле " + field);
            if (property.isArray)
            {
                Require(property.arraySize > 0, owner.name + ": пустой массив " + field);
                for (int i = 0; i < property.arraySize; i++)
                    Require(property.GetArrayElementAtIndex(i).objectReferenceValue != null,
                        owner.name + ": пустая ссылка " + field + "[" + i + "]");
            }
            else Require(property.objectReferenceValue != null, owner.name + ": пустая ссылка " + field);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
