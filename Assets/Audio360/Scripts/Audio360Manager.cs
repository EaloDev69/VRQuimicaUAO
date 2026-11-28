using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// Audio 360 de Molécula Café. NO hay que ponerlo en la escena: al dar Play se crea solo (objeto "Audio360")
/// con la librería de Audio360/Resources/SoundLibrary360, la música por capas y el conector de eventos.
///
/// Cómo suena en 360: cada efecto sale de una fuente colocada en el punto del mundo donde ocurre (el botón,
/// la palanca, la boquilla del spawner, la máquina, la pantalla de pedidos...), con spatialBlend = 1 y
/// atenuación logarítmica. El AudioListener de la Main Camera (la cabeza) hace el resto: si la máquina está
/// detrás, se oye detrás. Para HRTF del Quest (arriba/abajo): instalar Meta XR Audio SDK, elegirlo en
/// Project Settings > Audio > Spatializer Plugin y marcar "Use Spatializer Plugin" en este componente.
public class Audio360Manager : MonoBehaviour
{
    public static Audio360Manager Instance { get; private set; }

    public SoundLibrary360 library;

    [Header("Espacialización")]
    public bool useSpatializerPlugin = false;
    public AudioRolloffMode rolloff = AudioRolloffMode.Logarithmic;

    [Header("Volumen general")]
    [Range(0f, 1f)] public float sfxVolume = 1f;
    [Range(0f, 1f)] public float uiVolume = 1f;
    public int poolSize = 24;

    readonly List<AudioSource> pool = new List<AudioSource>();
    int next;

    // ---------------- Arranque automático ----------------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<Audio360Manager>() != null) return;
        var lib = Resources.Load<SoundLibrary360>("SoundLibrary360");
        if (lib == null) { Debug.LogWarning("[Audio360] No encontré Resources/SoundLibrary360: el audio 360 queda apagado."); return; }

        var go = new GameObject("Audio360");
        DontDestroyOnLoad(go);
        var m = go.AddComponent<Audio360Manager>();
        m.library = lib;
        m.Init();
        go.AddComponent<Audio360Music>().library = lib;
        go.AddComponent<Audio360Binder>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start() { if (pool.Count == 0) Init(); }

    void Init()
    {
        Instance = this;
        if (pool.Count > 0) return;
        for (int i = 0; i < poolSize; i++) pool.Add(NewSource($"Sfx_{i}", transform));
    }

    AudioSource NewSource(string n, Transform parent)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.dopplerLevel = 0f;                 // en VR el Doppler marea
        s.rolloffMode = rolloff;
        return s;
    }

    void Configure(AudioSource s, SoundLibrary360.Entry e, AudioClip clip, float pitchMul)
    {
        s.clip = clip;
        s.outputAudioMixerGroup = library.GroupFor(e.bus);
        s.volume = e.volume * (e.bus == SoundBus360.Ui ? uiVolume : sfxVolume);
        s.pitch = Random.Range(e.pitchRange.x, Mathf.Max(e.pitchRange.x, e.pitchRange.y)) * pitchMul;
        s.spatialBlend = e.spatial ? 1f : 0f;
        s.spatialize = e.spatial && useSpatializerPlugin;
        s.minDistance = e.minDistance;
        s.maxDistance = e.maxDistance;
        s.rolloffMode = rolloff;
    }

    // ---------------- API ----------------
    public static void Play(SoundId360 id, Vector3 pos, string key = null, float pitchMul = 1f)
    {
        if (Instance != null) Instance.PlayAt(id, pos, key, pitchMul);
    }

    public static void Play(SoundId360 id, Transform at, string key = null, float pitchMul = 1f)
    {
        if (Instance == null) return;
        Vector3 p = at != null ? at.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
        Instance.PlayAt(id, p, key, pitchMul);
    }

    public AudioSource PlayAt(SoundId360 id, Vector3 pos, string key, float pitchMul)
    {
        if (library == null) return null;
        var e = library.Get(id, key);
        if (e == null) return null;
        var clip = e.PickClip();
        if (clip == null) return null;
        if (Time.time - e.lastPlayed < e.cooldown) return null;
        e.lastPlayed = Time.time;

        AudioSource s = null;
        for (int i = 0; i < pool.Count; i++)
        {
            var c = pool[(next + i) % pool.Count];
            if (!c.isPlaying) { s = c; next = (next + i + 1) % pool.Count; break; }
        }
        if (s == null) { s = pool[next]; next = (next + 1) % pool.Count; }

        Configure(s, e, clip, pitchMul);
        s.loop = false;
        s.transform.position = pos;
        s.Play();
        return s;
    }

    /// Bucle pegado a un objeto (zumbido de la máquina, chorro). Devuelve la fuente para pararla con StopLoop.
    public AudioSource StartLoop(SoundId360 id, Transform attach, string key = null)
    {
        if (library == null || attach == null) return null;
        var e = library.Get(id, key);
        var clip = e?.PickClip();
        if (clip == null) return null;
        var s = NewSource($"Loop_{id}", attach);
        Configure(s, e, clip, 1f);
        s.loop = true;
        s.Play();
        return s;
    }

    public void StopLoop(AudioSource s, float fade = 0.4f) { if (s != null) StartCoroutine(FadeOut(s, fade)); }

    IEnumerator FadeOut(AudioSource s, float fade)
    {
        float v0 = s.volume;
        for (float t = 0f; t < 1f && s != null; t += Time.deltaTime / Mathf.Max(0.01f, fade))
        {
            s.volume = Mathf.Lerp(v0, 0f, t);
            yield return null;
        }
        if (s != null) Destroy(s.gameObject);
    }
}
