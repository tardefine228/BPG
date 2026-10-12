using System;
using System.Linq;
using TMPro;
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
        ValidateMouseStarRules();
        ValidateStarUnlockRules();
        ValidateLevelTaglines();
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

                References(Find<SceneTransition>().Single(), "settings", "group", "progressFill", "caption", "percent", "backgroundImage");
                if (scene.name != "Boot")
                {
                    var transition = Find<SceneTransition>().Single();
                    var names = transition.GetComponentsInChildren<Transform>(true).Select(child => child.name).ToArray();
                    Require(new[] { "AccentLine", "LogoPlaceholder", "GameTitle", "StudioText" }.All(name => !names.Contains(name)),
                        scene.name + ": лишние объекты на панели загрузки");
                }
                foreach (var splash in Find<StartupController>())
                    References(splash, "settings", "screenGroup", "brandingGroup", "backgroundImage", "logoImage", "companyNameText");
                foreach (var guide in Find<FirstLevelGuide>())
                    References(guide, "overlayFade", "firstIllustration", "secondIllustration", "gameplayController", "pauseButton");
                foreach (var audio in Find<AudioManager>()) References(audio, "musicSource", "effectsSource", "uiSource");
                foreach (var session in Find<LevelSession>())
                {
                    var sessionData = new SerializedObject(session);
                    Require(sessionData.FindProperty("maxProjectiles").intValue >= 1,
                        scene.name + ": боезапас должен быть положительным");
                    Require(sessionData.FindProperty("resultDelayAfterLastShot").floatValue >= 5f,
                        scene.name + ": оценка результата должна ждать минимум пять секунд");
                    References(session, "gameplayController", "starOnSprite", "starOffSprite", "scoreValueText",
                        "scoreHintText", "completionOverlay", "defeatOverlay",
                        "completionScoreText", "completionShotsText", "completionStars", "victorySource");
                    ValidateCompletionMenuButton(session);
                    // Уровень заканчивается победой, когда убита последняя обязательная мышь,
                    // поэтому хотя бы одна включённая мышь на карте должна считать за победу.
                    Require(Find<MouseEnemy>().Any(enemy => enemy.CountsForVictory && enemy.gameObject.activeInHierarchy),
                        scene.name + ": нет обязательных мышей — уровень не закончится по мышам");
                    References(Find<PauseMenuController>().Single(), "pausePage", "settingsPage", "pauseButton",
                        "musicVolumeSlider", "effectsVolumeSlider", "overlayFade", "pauseFade", "settingsFade");
                    ValidatePauseHierarchy(Find<PauseMenuController>().Single());
                    References(Find<GameplayCameraController>().Single(), "background");
                    var atmosphere = Find<ForestAtmosphere>().Single();
                    References(atmosphere, "atmosphereMaterial");
                    Require(atmosphere.GetComponent<Camera>() != null, scene.name + ": атмосфера должна быть на камере");
                    var atmosphereMaterial = (Material)new SerializedObject(atmosphere)
                        .FindProperty("atmosphereMaterial").objectReferenceValue;
                    Require(atmosphereMaterial.shader != null && !ShaderUtil.ShaderHasError(atmosphereMaterial.shader),
                        scene.name + ": ошибка шейдера атмосферы");
                    Require(Find<GameplayCameraController>().Single().TerrainCollider != null,
                        scene.name + ": не назначена земля для очереди и мышей");
                    if (scene.name == "Gameplay" || scene.name == "Level2")
                    {
                        Require(!Find<Transform>().Any(item => item.name == "FlatGround"), "Прежний пол не удалён");
                        Require(Find<GameplayCameraController>().Single().TerrainCollider is UnityEngine.Tilemaps.TilemapCollider2D
                            && Find<GameplayCameraController>().Single().TerrainCollider.name == "Layer1",
                            "Земля должна ссылаться на плотный Layer1");
                    }
                    var preview = Find<GameplayCameraController>().Single();
                    Require(Vector2.Distance(preview.transform.position, (preview.mapMin + preview.mapMax) * 0.5f) < 0.01f,
                        scene.name + ": редакторская камера не совпадает с игровым кадром");
                    Require(!Find<Transform>().Any(item => item.name == "GameplayCameraCanvas"),
                        scene.name + ": остались переключатели камеры");
                    References(Find<SlingshotController>().Single(), "dots");
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
                    References(card, "button", "preview", "currentHighlight", "lockOverlay", "lockLabel", "soonOverlay", "parHint", "stars");
                    if (card.LevelId >= 1 && card.LevelId <= 5)
                        References(card, "hoverOutline");
                    var highlight = (GameObject)new SerializedObject(card).FindProperty("currentHighlight").objectReferenceValue;
                    if (highlight.transform.parent == card.transform.parent)
                        Require(highlight.transform.GetSiblingIndex() < card.transform.GetSiblingIndex(),
                            card.name + ": подсветка перекрывает карточку");
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
            var mouse = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mouses/EasyEnemy.prefab");
            Require(mouse.GetComponent<BoxCollider2D>() != null && mouse.GetComponent<CircleCollider2D>() == null,
                "Мышь должна использовать прямоугольный коллайдер");
            References(mouse.GetComponent<MouseEnemy>(), "deathSprite");
            var fur = AssetDatabase.LoadAssetAtPath<ParticleSystem>("Assets/Prefabs/Cats/FurTrail.prefab");
            Require(fur != null && !fur.main.playOnAwake
                && fur.main.simulationSpace == ParticleSystemSimulationSpace.World,
                "Шерсть: неверные настройки префаба частиц");
            References(fur.GetComponent<ParticleSystemRenderer>(), "m_Materials");
            ValidateMainPrototype();
            Debug.Log("Все связи интерфейса и эффектов проверены.");
        }
        finally
        {
            if (setup.Any(scene => scene.isActive && scene.isLoaded))
                EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    private static T[] Find<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include);

    private static void ValidateCompletionMenuButton(LevelSession session)
    {
        var serialized = new SerializedObject(session);
        var completion = (GameObject)serialized.FindProperty("completionOverlay").objectReferenceValue;
        var button = completion.GetComponentsInChildren<Button>(true)
            .FirstOrDefault(item => item.name == "ToMenuButton");
        string sceneName = session.gameObject.scene.name;
        Require(button != null, sceneName + ": на экране прохождения нет кнопки ToMenuButton");
        Require(button.gameObject.activeSelf, sceneName + ": кнопка МЕНЮ выключена в иерархии экрана прохождения");

        var label = button.GetComponentInChildren<TMP_Text>(true);
        Require(label != null && label.text.Trim() == "МЕНЮ",
            sceneName + ": у кнопки выбора уровня должна быть подпись МЕНЮ");
        bool returnsToLevelSelect = Enumerable.Range(0, button.onClick.GetPersistentEventCount())
            .Any(index => button.onClick.GetPersistentTarget(index) == session
                && button.onClick.GetPersistentMethodName(index) == nameof(LevelSession.ReturnToLevelSelect));
        Require(returnsToLevelSelect,
            sceneName + ": кнопка МЕНЮ должна возвращать к выбору уровней");
    }

    private static void ValidateMainPrototype()
    {
        const string path = "Assets/Scenes/Gameplay_MainPrototype.unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            return;
        var scene = EditorSceneManager.OpenScene(path);
        foreach (var root in scene.GetRootGameObjects())
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0,
                path + ": отсутствует скрипт на " + child.name);
            Require(PrefabUtility.GetPrefabInstanceStatus(child.gameObject) != PrefabInstanceStatus.MissingAsset,
                path + ": отсутствует префаб " + child.name);
        }
        Require(Find<LevelTarget>().Length >= 15 && Find<MouseEnemy>().Length >= 5,
            "Прототип main потерял часть постройки или мышей");
        foreach (var button in Find<Button>())
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            var target = button.onClick.GetPersistentTarget(i);
            Require(target != null && target.GetType().GetMethods().Any(method =>
                method.Name == button.onClick.GetPersistentMethodName(i)),
                path + ": потерян обработчик кнопки " + button.name);
        }
        Debug.Log("PROTOTYPE CHECK OK: main objects and current UI preserved");
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

    private static void ValidateMouseStarRules()
    {
        Require(LevelProgress.StarsForDefeatedMice(0, 8) == 0,
            "Звёзды за мышей: без убийств должен быть нулевой результат");
        Require(LevelProgress.StarsForDefeatedMice(1, 2) == 2,
            "Звёзды за мышей: половина из двух мышей должна давать две звезды");
        Require(LevelProgress.StarsForDefeatedMice(1, 4) == 1
            && LevelProgress.StarsForDefeatedMice(2, 4) == 2,
            "Звёзды за мышей: округление доли убийств работает неверно");
        Require(LevelProgress.StarsForDefeatedMice(3, 4) == 2
            && LevelProgress.StarsForDefeatedMice(6, 7) == 2,
            "Звёзды за мышей: неполное прохождение не должно давать три звезды");
        Require(LevelProgress.StarsForDefeatedMice(1, 7) == 1
            && LevelProgress.StarsForDefeatedMice(1, 8) == 1,
            "Звёзды за мышей: одно убийство не должно округляться до нуля");
        Require(LevelProgress.StarsForDefeatedMice(7, 8) == 2
            && LevelProgress.StarsForDefeatedMice(7, 7) == 3
            && LevelProgress.StarsForDefeatedMice(8, 8) == 3,
            "Звёзды за мышей: три звезды требуют уничтожить всех обязательных мышей");
        Require(LevelProgress.StarsForDefeatedMice(9, 8) == 3
            && LevelProgress.StarsForDefeatedMice(-1, 8) == 0
            && LevelProgress.StarsForDefeatedMice(0, 0) == 0,
            "Звёзды за мышей: некорректные границы количества не обработаны");
    }

    // Доступ к уровням: первые два открываются последовательно, третий, четвёртый
    // и пятый ждут 5, 8 и 12 собранных звёзд.
    private static void ValidateStarUnlockRules()
    {
        Require(LevelProgress.RequiredStarsFor(1) == 0 && LevelProgress.RequiredStarsFor(2) == 0,
            "Доступ к уровням: у первых двух уровней порога звёзд быть не должно");
        Require(LevelProgress.RequiredStarsFor(3) == 5,
            "Доступ к уровням: третий уровень открывается за 5 звёзд");
        Require(LevelProgress.RequiredStarsFor(4) == 8,
            "Доступ к уровням: четвёртый уровень открывается за 8 звёзд");
        Require(LevelProgress.RequiredStarsFor(5) == 12,
            "Доступ к уровням: пятый уровень открывается за 12 звёзд");
        Require(LevelProgress.Levels.Length * LevelProgress.MaxStars >= LevelProgress.RequiredStarsFor(5),
            "Доступ к уровням: всех уровней должно хватать на порог пятого");

        Require(LevelProgress.IsUnlockedByRules(1, 0, false),
            "Доступ к уровням: первый уровень открыт всегда");
        Require(LevelProgress.IsUnlockedByRules(2, 0, true) && !LevelProgress.IsUnlockedByRules(2, 0, false),
            "Доступ к уровням: второй уровень открывается после первого");
        Require(!LevelProgress.IsUnlockedByRules(3, 4, true) && LevelProgress.IsUnlockedByRules(3, 5, true),
            "Доступ к уровням: третий уровень ждёт ровно 5 звёзд");
        Require(!LevelProgress.IsUnlockedByRules(4, 7, true) && LevelProgress.IsUnlockedByRules(4, 8, true),
            "Доступ к уровням: четвёртый уровень ждёт ровно 8 звёзд");
        Require(!LevelProgress.IsUnlockedByRules(5, 11, true) && LevelProgress.IsUnlockedByRules(5, 12, true),
            "Доступ к уровням: пятый уровень ждёт ровно 12 звёзд");
        Require(LevelProgress.IsUnlockedByRules(3, 5, false) && LevelProgress.IsUnlockedByRules(5, 12, false),
            "Доступ к уровням: порог звёзд не должен требовать ещё и пройденного предыдущего уровня");
    }

    // Названия уровней под звёздами карточки: непустые, короткие и без повторов.
    private static void ValidateLevelTaglines()
    {
        var taglines = LevelProgress.Levels.Select(level => level.Tagline).ToArray();
        foreach (string tagline in taglines)
            Require(!string.IsNullOrWhiteSpace(tagline) && tagline.Length <= 18,
                "Название уровня на карточке должно быть непустым и коротким (до 18 символов): «" + tagline + "»");
        Require(taglines.Distinct().Count() == taglines.Length,
            "Названия уровней на карточках не должны повторяться: " + string.Join(" / ", taglines));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
