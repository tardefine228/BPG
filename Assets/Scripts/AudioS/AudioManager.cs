using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Менеджер музыки и эффектов сохраняется между сценами. Ползунки регулируют музыку и эффекты отдельно.
[DisallowMultipleComponent]
public sealed class AudioManager : MonoBehaviour
{
    private const string DefaultMusicVolumeKey = "bpg_audio_music_volume";
    private const string DefaultEffectsVolumeKey = "bpg_audio_effects_volume";

    public static AudioManager Instance { get; private set; }

    [Header("Музыка")]
    [Tooltip("Добавьте сюда заранее импортированные AudioClip. Треки будут проигрываться по порядку.")]
    [SerializeField] private AudioClip[] musicTracks = new AudioClip[0];
    [SerializeField] private bool loopPlaylist = true;
    [SerializeField] private bool playMusicOnStart = true;

    [Header("Звуки интерфейса (громкость эффектов)")]
    [SerializeField] private AudioClip uiClickSound;
    [SerializeField] private AudioClip uiHoverSound;
    [SerializeField] private AudioClip uiOpenSound;
    [SerializeField] private AudioClip uiCloseSound;

    [Header("Звук выстрела рогатки (задаётся в сцене уровня, громкость эффектов)")]
    [Tooltip("Перетащите звук выстрела рогатки сюда на AudioManager в сцене уровня. Пустое поле оставляет выстрел без звука.")]
    [SerializeField] private AudioClip shotSound;
    [SerializeField, Range(0f, 1f)] private float shotSoundVolume = 1f;

    [Header("Громкость")]
    [SerializeField, Range(0f, 1f)] private float defaultMusicVolume = 0.5f;
    [SerializeField, Range(0f, 1f)] private float defaultEffectsVolume = 0.5f;
    [SerializeField] private bool saveVolumeSettings = true;
    [SerializeField] private string musicVolumePrefsKey = DefaultMusicVolumeKey;
    [SerializeField] private string effectsVolumePrefsKey = DefaultEffectsVolumeKey;
    [SerializeField, Min(0f)] private float preferenceSaveDelay = 0.5f;

    [Header("Источники звука")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource effectsSource;
    [SerializeField] private AudioSource uiSource;

    private readonly Dictionary<AudioSource, float> effectsSourceBaseVolumes =
        new Dictionary<AudioSource, float>();

    private float musicVolume;
    private float effectsVolume;
    private int currentTrackIndex = -1;
    private bool playlistIsActive;
    private bool isDuplicate;
    private Coroutine preferenceSaveRoutine;
    private int lastUiClickFrame = -1;

    public float MusicVolume => musicVolume;
    public float EffectsVolume => effectsVolume;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Главное меню сохраняет менеджер, а уровень передаёт ему свой звук выстрела.
            if (LevelProgress.IsGameplayScene(gameObject.scene.name))
            {
                Instance.shotSound = shotSound;
                Instance.shotSoundVolume = shotSoundVolume;
            }
            isDuplicate = true;
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        musicSource = ConfigureSource(musicSource, "MusicSource");
        effectsSource = ConfigureSource(effectsSource, "EffectsSource");
        uiSource = ConfigureSource(uiSource, "InterfaceSource");
        uiSource.ignoreListenerPause = true;

        string musicKey = string.IsNullOrWhiteSpace(musicVolumePrefsKey)
            ? DefaultMusicVolumeKey
            : musicVolumePrefsKey;
        string effectsKey = string.IsNullOrWhiteSpace(effectsVolumePrefsKey)
            ? DefaultEffectsVolumeKey
            : effectsVolumePrefsKey;

        musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(musicKey, defaultMusicVolume));
        effectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(effectsKey, defaultEffectsVolume));

        musicSource.volume = musicVolume;
        ApplyEffectsVolumeToAllSources();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private IEnumerator Start()
    {
        if (isDuplicate)
            yield break;
        while (SceneTransition.StartupVisible)
            yield return null;
        if (playMusicOnStart)
        {
            PlayPlaylist();
        }
    }

    private void Update()
    {
        if (playlistIsActive && musicSource != null && musicSource.clip != null && !musicSource.isPlaying)
        {
            PlayNextTrack();
        }
    }

    private void OnDestroy()
    {
        if (isDuplicate || Instance != this)
        {
            return;
        }

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SaveVolumePreferences();
        Instance = null;
    }

    private void OnApplicationPause(bool isPaused)
    {
        if (isPaused)
        {
            SaveVolumePreferences();
        }
    }

    private void OnApplicationQuit()
    {
        SaveVolumePreferences();
    }

    // Запускает плейлист с первого доступного трека.
    public void PlayPlaylist()
    {
        if (musicSource == null)
        {
            return;
        }

        musicSource.Stop();
        currentTrackIndex = -1;
        playlistIsActive = true;
        PlayNextTrack();
    }

    // Переключает плейлист на следующий доступный трек.
    public void PlayNextTrack()
    {
        if (musicSource == null || musicTracks == null || musicTracks.Length == 0)
        {
            StopMusic();
            return;
        }

        int startIndex = currentTrackIndex + 1;
        for (int offset = 0; offset < musicTracks.Length; offset++)
        {
            int trackIndex = startIndex + offset;
            if (loopPlaylist)
            {
                trackIndex %= musicTracks.Length;
            }
            else if (trackIndex >= musicTracks.Length)
            {
                break;
            }

            AudioClip track = musicTracks[trackIndex];
            if (track == null)
            {
                continue;
            }

            currentTrackIndex = trackIndex;
            musicSource.clip = track;
            musicSource.loop = false;
            musicSource.Play();
            playlistIsActive = true;
            return;
        }

        StopMusic();
    }

    // Останавливает музыку, сохраняя выбранную громкость.
    public void StopMusic()
    {
        playlistIsActive = false;
        currentTrackIndex = -1;

        if (musicSource != null)
        {
            musicSource.Stop();
            musicSource.clip = null;
        }
    }

    public void SetMusicVolume(float volume)
    {
        float newVolume = Mathf.Clamp01(volume);
        if (Mathf.Approximately(musicVolume, newVolume))
        {
            return;
        }

        musicVolume = newVolume;
        if (musicSource != null)
        {
            musicSource.volume = musicVolume;
        }

        ScheduleVolumePreferenceSave();
    }

    public void SetEffectsVolume(float volume)
    {
        float newVolume = Mathf.Clamp01(volume);
        if (Mathf.Approximately(effectsVolume, newVolume))
        {
            return;
        }

        effectsVolume = newVolume;
        ApplyEffectsVolumeToAllSources();
        ScheduleVolumePreferenceSave();
    }

    // Проигрывает эффект через общий канал; громкость подчиняется настройке эффектов.
    public void PlaySfx(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null || effectsSource == null)
        {
            return;
        }

        effectsSource.PlayOneShot(clip, Mathf.Max(0f, volumeScale));
    }

    // Запускает эффект на отдельном AudioSource, чтобы вызывающий компонент
    // мог остановить его без прерывания остальных эффектов.
    public void PlaySfxFromSource(AudioSource source, AudioClip clip, float volumeScale = 1f)
    {
        if (source == null || clip == null)
            return;

        float baseVolume = Mathf.Max(0f, volumeScale);
        effectsSourceBaseVolumes[source] = baseVolume;
        source.volume = baseVolume * effectsVolume;
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.Stop();
        source.clip = clip;
        source.Play();
    }

    public void PlayShotSound() => PlaySfx(shotSound, shotSoundVolume);

    // Звуки интерфейса подчиняются громкости эффектов и работают во время паузы.
    public void PlayUiSound(AudioClip clip, float volumeScale = 1f)
    {
        if (clip != null && uiSource != null)
            uiSource.PlayOneShot(clip, Mathf.Max(0f, volumeScale));
    }

    public void PlayUiClick()
    {
        if (lastUiClickFrame == Time.frameCount)
            return;
        lastUiClickFrame = Time.frameCount;
        PlayUiSound(uiClickSound);
    }
    public void PlayUiHover() => PlayUiSound(uiHoverSound);
    public void PlayUiOpen() => PlayUiSound(uiOpenSound != null ? uiOpenSound : uiClickSound);
    public void PlayUiClose() => PlayUiSound(uiCloseSound != null ? uiCloseSound : uiClickSound);

    private static AudioSource ConfigureSource(AudioSource source, string sourceName)
    {
        if (source == null)
        {
            Debug.LogError("В сцене не назначен источник звука " + sourceName);
            return null;
        }
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        return source;
    }
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyEffectsVolumeToAllSources();
    }

    private void ApplyEffectsVolumeToAllSources()
    {
        AudioSource[] audioSources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include);

        for (int i = 0; i < audioSources.Length; i++)
        {
            AudioSource source = audioSources[i];
            if (source == null || source == musicSource)
            {
                continue;
            }

            if (!effectsSourceBaseVolumes.TryGetValue(source, out float baseVolume))
            {
                baseVolume = source.volume;
                effectsSourceBaseVolumes.Add(source, baseVolume);
            }

            source.volume = baseVolume * effectsVolume;
        }

        RemoveDestroyedSourcesFromCache();
    }

    private void RemoveDestroyedSourcesFromCache()
    {
        List<AudioSource> destroyedSources = null;
        foreach (KeyValuePair<AudioSource, float> entry in effectsSourceBaseVolumes)
        {
            if (entry.Key == null)
            {
                if (destroyedSources == null)
                {
                    destroyedSources = new List<AudioSource>();
                }

                destroyedSources.Add(entry.Key);
            }
        }

        if (destroyedSources == null)
        {
            return;
        }

        for (int i = 0; i < destroyedSources.Count; i++)
        {
            effectsSourceBaseVolumes.Remove(destroyedSources[i]);
        }
    }

    private void ScheduleVolumePreferenceSave()
    {
        if (!saveVolumeSettings)
        {
            return;
        }

        PlayerPrefs.SetFloat(GetMusicPrefsKey(), musicVolume);
        PlayerPrefs.SetFloat(GetEffectsPrefsKey(), effectsVolume);

        if (preferenceSaveRoutine != null)
        {
            StopCoroutine(preferenceSaveRoutine);
        }

        preferenceSaveRoutine = StartCoroutine(SaveVolumePreferencesAfterDelay());
    }

    private IEnumerator SaveVolumePreferencesAfterDelay()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, preferenceSaveDelay));
        preferenceSaveRoutine = null;
        SaveVolumePreferences();
    }

    private void SaveVolumePreferences()
    {
        if (!saveVolumeSettings)
        {
            return;
        }

        if (preferenceSaveRoutine != null)
        {
            StopCoroutine(preferenceSaveRoutine);
            preferenceSaveRoutine = null;
        }

        PlayerPrefs.SetFloat(GetMusicPrefsKey(), musicVolume);
        PlayerPrefs.SetFloat(GetEffectsPrefsKey(), effectsVolume);
        PlayerPrefs.Save();
    }

    private string GetMusicPrefsKey()
    {
        return string.IsNullOrWhiteSpace(musicVolumePrefsKey)
            ? DefaultMusicVolumeKey
            : musicVolumePrefsKey;
    }

    private string GetEffectsPrefsKey()
    {
        return string.IsNullOrWhiteSpace(effectsVolumePrefsKey)
            ? DefaultEffectsVolumeKey
            : effectsVolumePrefsKey;
    }
}
