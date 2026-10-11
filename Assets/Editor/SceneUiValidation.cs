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
                        "scoreHintText", "ammoValueText", "completionOverlay", "defeatOverlay",
                        "completionScoreText", "completionShotsText", "completionStars", "victorySource");
                    References(Find<PauseMenuController>().Single(), "pausePage", "settingsPage", "pauseButton",
                        "musicVolumeSlider", "effectsVolumeSlider", "overlayFade", "pauseFade", "settingsFade");
                    References(Find<GameplayCameraController>().Single(), "controlsCanvas", "controlsRect",
                        "launcherButton", "overviewButton", "targetButton");
                    References(Find<SlingshotController>().Single(), "dots");
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
                    References(card, "button", "preview", "currentHighlight", "lockOverlay", "soonOverlay", "parHint", "stars");
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
            Debug.Log("Все связи интерфейса и эффектов проверены.");
        }
        finally
        {
            if (setup.Any(scene => scene.isActive && scene.isLoaded))
                EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }

    private static T[] Find<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include);

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
