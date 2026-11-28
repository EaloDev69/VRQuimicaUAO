using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// Librería de sonidos del audio 360. Está en Audio360/Resources/SoundLibrary360 y el sistema la carga solo.
/// Para cambiar un sonido: arrastra otro clip a la entrada (o reemplaza el archivo con el mismo nombre).
/// "key" = variante: PortClick por elemento (H, O, C...) y RecipeEffect por receta (H2O, HCL, CH4...).
[CreateAssetMenu(menuName = "Molecula/Audio 360/Sound Library")]
public class SoundLibrary360 : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public SoundId360 id;
        public string key;
        public AudioClip[] clips;
        public SoundBus360 bus = SoundBus360.Sfx;
        [Range(0f, 1f)] public float volume = 0.8f;
        public Vector2 pitchRange = new Vector2(0.95f, 1.05f);
        [Tooltip("true = sale del punto del mundo donde ocurre (360). false = interfaz")]
        public bool spatial = true;
        public float minDistance = 0.4f;
        public float maxDistance = 8f;
        public float cooldown = 0.03f;

        [NonSerialized] public float lastPlayed = -999f;
        public AudioClip PickClip() => clips == null || clips.Length == 0 ? null : clips[UnityEngine.Random.Range(0, clips.Length)];
    }

    [Header("Música (lo-fi por capas)")]
    public AudioClip musicBase;
    public AudioClip musicRhythm;            // misma duración y tempo que la base
    [Range(0f, 1f)] public float musicVolume = 0.35f;

    [Header("Mixer (opcional)")]
    public AudioMixerGroup musicGroup;
    public AudioMixerGroup sfxGroup;
    public AudioMixerGroup uiGroup;

    public List<Entry> entries = new List<Entry>();

    Dictionary<(SoundId360, string), Entry> map;
    void OnEnable() { map = null; }
    void OnValidate() { map = null; }

    public Entry Get(SoundId360 id, string key = null)
    {
        if (map == null)
        {
            map = new Dictionary<(SoundId360, string), Entry>();
            foreach (var e in entries) if (e != null) map[(e.id, (e.key ?? "").ToUpperInvariant())] = e;
        }
        if (!string.IsNullOrEmpty(key) && map.TryGetValue((id, key.ToUpperInvariant()), out var k) && k.PickClip() != null) return k;
        return map.TryGetValue((id, ""), out var d) ? d : null;
    }

    public AudioMixerGroup GroupFor(SoundBus360 b) => b == SoundBus360.Music ? musicGroup : b == SoundBus360.Ui ? uiGroup : sfxGroup;
}
