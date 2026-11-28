using UnityEngine;

/// Música lo-fi en dos capas sincronizadas: la base suena siempre; la capa rítmica sube cuando queda poco tiempo
/// en un pedido y baja al entregarlo o perderlo. Se crea sola con Audio360Manager.
public class Audio360Music : MonoBehaviour
{
    public SoundLibrary360 library;
    public float fadeSeconds = 1.5f;

    AudioSource baseSrc, rhythmSrc;
    float target;            // 0 tranquilo, 1 intenso
    float intenseUntil;
    OrderManager orders;
    float nextSearch;

    void Start()
    {
        if (library == null || library.musicBase == null) { enabled = false; return; }
        baseSrc = Make("Music_Base", library.musicBase);
        rhythmSrc = Make("Music_Rhythm", library.musicRhythm);
        double t = AudioSettings.dspTime + 0.2;
        baseSrc.volume = library.musicVolume;
        baseSrc.PlayScheduled(t);
        if (rhythmSrc.clip != null) { rhythmSrc.volume = 0f; rhythmSrc.PlayScheduled(t); }   // misma hora DSP = sincronía exacta
    }

    AudioSource Make(string n, AudioClip clip)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.clip = clip; s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f;   // la música no es 3D
        s.outputAudioMixerGroup = library.musicGroup;
        return s;
    }

    void Update()
    {
        // engancha el OrderManager de la escena actual (también tras recargar escena)
        if (orders == null && Time.time >= nextSearch)
        {
            nextSearch = Time.time + 1f;
            orders = FindFirstObjectByType<OrderManager>();
            if (orders != null)
            {
                orders.onLowTime.AddListener(() => intenseUntil = Time.time + 60f);
                orders.OrderDelivered += _ => intenseUntil = 0f;
                orders.OrderLost += _ => intenseUntil = 0f;
            }
        }

        target = Time.time < intenseUntil ? 1f : 0f;
        if (rhythmSrc != null && rhythmSrc.clip != null)
            rhythmSrc.volume = Mathf.MoveTowards(rhythmSrc.volume, library.musicVolume * target,
                                                 library.musicVolume * Time.deltaTime / fadeSeconds);
    }

    /// Para subir la intensidad desde otros scripts (eventos, alarmas)
    public void Intensify(float seconds) => intenseUntil = Mathf.Max(intenseUntil, Time.time + seconds);
}
