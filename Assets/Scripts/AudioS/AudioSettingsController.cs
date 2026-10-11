using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Connects the music and effects sliders in the settings panel to AudioManager.
/// </summary>
public sealed class AudioSettingsController : MonoBehaviour
{
    [SerializeField] private AudioManager audioManager;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private Slider effectsVolumeSlider;

    private void Start()
    {
        if (audioManager == null)
        {
            audioManager = AudioManager.Instance;
        }

        if (audioManager == null)
        {
            Debug.LogError("AudioSettingsController: AudioManager не найден.", this);
            return;
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.SetValueWithoutNotify(audioManager.MusicVolume);
            musicVolumeSlider.onValueChanged.AddListener(audioManager.SetMusicVolume);
        }

        if (effectsVolumeSlider != null)
        {
            effectsVolumeSlider.SetValueWithoutNotify(audioManager.EffectsVolume);
            effectsVolumeSlider.onValueChanged.AddListener(audioManager.SetEffectsVolume);
        }
    }

    private void OnDestroy()
    {
        if (audioManager == null)
        {
            return;
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.onValueChanged.RemoveListener(audioManager.SetMusicVolume);
        }

        if (effectsVolumeSlider != null)
        {
            effectsVolumeSlider.onValueChanged.RemoveListener(audioManager.SetEffectsVolume);
        }
    }
}
