using UnityEngine;

// Все элементы пула уже находятся в сцене и не создаются во время попадания.
public sealed class ComicEffectsPool : MonoBehaviour
{
    [SerializeField] private ComicParticleBurst[] bursts;
    private int cursor;
    public static ComicEffectsPool Instance { get; private set; }

    private void Awake() => Instance = this;

    private void Start()
    {
        Vector3 position = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        position.z = 0f;
        foreach (ComicParticleBurst burst in bursts)
            burst.WarmUp(position);
        // Подготавливает также первый поиск свободного эффекта.
        Next();
    }

    public ComicParticleBurst Next()
    {
        for (int i = 0; i < bursts.Length; i++)
        {
            int index = (cursor + i) % bursts.Length;
            if (bursts[index].IsPlaying) continue;
            cursor = (index + 1) % bursts.Length;
            return bursts[index];
        }
        // При очень частых ударах повторно используется самый старый слот.
        ComicParticleBurst oldest = bursts[cursor];
        cursor = (cursor + 1) % bursts.Length;
        return oldest;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
