using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class EffectToggleButton : MonoBehaviour
{
    [Header("Текст")]
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private string prefix = "Эффекты: ";
    [SerializeField] private string onText = "ВКЛ";
    [SerializeField] private string offText = "ВЫКЛ";

    [Header("Состояние")]
    [SerializeField] private bool defaultOn = true;
    [SerializeField] private bool saveState = true;
    [SerializeField] private string saveKey = "effects_on";

    [Header("Что включаем/выключаем")]
    [Tooltip("Простые объекты: партиклы, декорации, лишние камеры и т.п.")]
    [SerializeField] private List<GameObject> effectObjects = new(); 

    [Tooltip("Сюда вешай сложную логику: постпроцесс, звук, шейдеры.")]
    [SerializeField] private UnityEvent onStateChanged;

    private bool isOn;

    private void Start()
    {
        isOn = defaultOn;

        if (saveState && PlayerPrefs.HasKey(saveKey))
        {
            isOn = PlayerPrefs.GetInt(saveKey, 1) == 1;
        }

        ApplyState();
    }

    // Вешаем на кнопку Button, выбираем OnClick. Эт сам переключатель
    public void Toggle()
    {
        isOn = !isOn;
        ApplyState();
    }

    private void ApplyState()
    {
        if (labelText != null)
        {
            labelText.text = prefix + (isOn ? onText : offText);
        }

        foreach (var obj in effectObjects)
        {
            if (obj != null)
            {
                obj.SetActive(isOn);
            }
        }

        onStateChanged?.Invoke();

        if (saveState)
        {
            PlayerPrefs.SetInt(saveKey, isOn ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}