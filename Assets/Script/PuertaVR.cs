using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// Puerta del laboratorio. Dos modos:
///  - AutomaticaDeslizante (por defecto): como una puerta automática real. Cuando la cabeza o las manos del jugador
///    se acercan, cada hoja se desliza hacia su lado (alejándose del centro de la puerta doble) y se cierra sola
///    cuando el jugador se aleja.
///  - ManijaGiratoria: la puerta gira sobre la bisagra al agarrar una manija invisible (grip).
/// Apaga el Animator de la hoja: su animación tenía posiciones fijas del mundo y movía las dos hojas al mismo sitio.
/// Montaje: este componente en cada hoja (la que tiene el BoxCollider). En puertas dobles arrastra a "Pair Center"
/// el objeto que queda entre las dos hojas.
public class PuertaVR : MonoBehaviour
{
    public enum Modo { AutomaticaDeslizante, ManijaGiratoria }
    public enum Hinge { Auto, BordeIzquierdo, BordeDerecho }

    [Header("Modo")]
    public Modo modo = Modo.AutomaticaDeslizante;

    [Header("Puerta doble")]
    [Tooltip("Centro de la puerta doble (entre las dos hojas). Cada hoja se abre hacia el lado contrario a este punto")]
    public Transform pairCenter;

    [Header("Automática (deslizante)")]
    [Tooltip("Distancia horizontal (m) desde el centro de la puerta a la que se abre al acercar la cabeza o una mano")]
    public float openDistance = 2.2f;
    [Tooltip("Distancia a la que se vuelve a cerrar (un poco mayor para que no tiemble)")]
    public float closeDistance = 2.8f;
    [Tooltip("Cuánto se desliza la hoja, como fracción de su ancho")]
    [Range(0.3f, 1.1f)] public float slideFraction = 0.95f;
    [Tooltip("Velocidad de apertura en metros por segundo")]
    public float slideSpeed = 1.4f;

    [Header("Manija (solo modo ManijaGiratoria)")]
    public Hinge hinge = Hinge.Auto;
    public float openAngle = 95f;
    public float speed = 160f;                   // grados por segundo
    public bool openAwayFromPlayer = true;
    [Range(0.02f, 0.4f)] public float handleInset = 0.1f;
    public float handleHeight = 1.0f;
    public float handleRadius = 0.12f;

    [Header("Feedback")]
    public AudioSource sound;
    [Range(0f, 1f)] public float hapticAmplitude = 0.35f;

    public bool IsOpen { get; private set; }

    // común
    Vector3 closedPos, doorCenter;
    Quaternion closedRot;
    Vector3 widthAxis, doorNormal;
    float halfWidth;

    // deslizante
    Vector3 slideDir;
    float slideAmount, slide;
    readonly List<Transform> trackers = new List<Transform>();
    Collider[] leafColliders;
    float nextTrackerRefresh;

    // giratoria
    Vector3 hingePoint;
    float angle, target, side = 1f;
    Transform handle;

    void Start()
    {
        var anim = GetComponent<Animator>();
        if (anim != null) anim.enabled = false;

        var col = GetComponent<Collider>();
        if (col == null) col = GetComponentInChildren<Collider>();
        if (col == null) { Debug.LogError("[PuertaVR] La puerta necesita un collider para medir su tamaño.", this); enabled = false; return; }

        closedPos = transform.position;
        closedRot = transform.rotation;

        // Medidas desde el collider (caja alineada con el mundo): eje ancho = el horizontal más largo
        var b = col.bounds;
        bool widthIsX = b.size.x >= b.size.z;
        widthAxis = widthIsX ? Vector3.right : Vector3.forward;
        doorNormal = widthIsX ? Vector3.forward : Vector3.right;
        halfWidth = (widthIsX ? b.size.x : b.size.z) * 0.5f;
        doorCenter = pairCenter != null ? pairCenter.position : b.center;

        if (modo == Modo.AutomaticaDeslizante) SetupSliding(b);
        else SetupHandle(b);
    }

    // ---------------------------------------------------------------
    // Automática deslizante
    // ---------------------------------------------------------------
    void SetupSliding(Bounds b)
    {
        // Se desliza hacia el lado contrario al centro de la puerta doble
        Vector3 away = Flat(b.center - doorCenter);
        float d = Vector3.Dot(away, widthAxis);
        slideDir = widthAxis * (Mathf.Abs(d) > 0.01f ? Mathf.Sign(d) : 1f);
        slideAmount = halfWidth * 2f * slideFraction;
        leafColliders = GetComponentsInChildren<Collider>();
        HideDuplicateLeaves(b);
        RefreshTrackers();
    }

    // Copias de la hoja que quedaron en el mismo sitio (p. ej. la puerta original del modelo Doors.fbx con su
    // propio collider). Como no se mueven, al abrir dejaban una "pared invisible" en la puerta: se desactivan.
    void HideDuplicateLeaves(Bounds b)
    {
        // a) Objetos hermanos con la MISMA malla que esta hoja y sin PuertaVR que estén en el marco de la puerta
        var mf = GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null && transform.parent != null)
        {
            foreach (var other in transform.parent.GetComponentsInChildren<MeshFilter>(true))
            {
                if (other == null || other.transform == transform || other.sharedMesh != mf.sharedMesh) continue;
                if (other.GetComponentInParent<PuertaVR>() != null) continue;
                var r = other.GetComponent<Renderer>();
                Vector3 p = r != null ? r.bounds.center : other.transform.position;
                if (Flat(p - doorCenter).magnitude > halfWidth * 3f) continue;           // solo si está en esta puerta
                foreach (var c in other.GetComponents<Collider>()) c.enabled = false;
                if (r != null) r.enabled = false;
                Debug.Log($"[PuertaVR] Desactivada una copia fija de la puerta ({other.name}) que bloqueaba el paso.", other);
            }
        }

        // b) Cualquier otro collider fijo del mismo tamaño en el mismo sitio
        foreach (var c in Physics.OverlapBox(b.center, b.extents * 0.9f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c == null || c.transform.IsChildOf(transform) || c.attachedRigidbody != null) continue;
            if (c.GetComponentInParent<PuertaVR>() != null) continue;                 // la otra hoja
            var cb = c.bounds;
            bool sameSize = (cb.size - b.size).magnitude <= 0.25f * b.size.magnitude;
            bool samePlace = (cb.center - b.center).magnitude <= 0.4f;
            if (!sameSize || !samePlace) continue;
            c.enabled = false;
            var r = c.GetComponent<Renderer>();
            if (r != null) r.enabled = false;
            Debug.Log($"[PuertaVR] Desactivada una copia fija de la puerta ({c.name}) que bloqueaba el paso.", c);
        }
    }

    // Cabeza (cámara) y manos/controles del jugador
    void RefreshTrackers()
    {
        trackers.Clear();
        if (Camera.main != null) trackers.Add(Camera.main.transform);
        foreach (var i in FindObjectsByType<XRBaseInputInteractor>(FindObjectsSortMode.None))
            if (i != null && i.isActiveAndEnabled) trackers.Add(i.transform);
        foreach (var cc in FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
            if (cc != null && cc.enabled) trackers.Add(cc.transform);      // el cuerpo del jugador
        nextTrackerRefresh = Time.time + 2f;
    }

    bool PlayerNear(float dist)
    {
        float d2 = dist * dist;
        foreach (var t in trackers)
            if (t != null && Flat(t.position - doorCenter).sqrMagnitude <= d2) return true;
        return false;
    }

    void UpdateSliding()
    {
        if (Time.time >= nextTrackerRefresh) RefreshTrackers();

        bool wantOpen = IsOpen ? PlayerNear(closeDistance) : PlayerNear(openDistance);
        if (wantOpen != IsOpen)
        {
            IsOpen = wantOpen;
            if (sound != null) sound.Play();
        }

        float goal = IsOpen ? slideAmount : 0f;
        if (!Mathf.Approximately(slide, goal))
        {
            slide = Mathf.MoveTowards(slide, goal, slideSpeed * Time.deltaTime);
            transform.position = closedPos + slideDir * slide;
        }

        // Mientras está abierta (o abriéndose/cerrándose) la hoja no bloquea el paso; solo bloquea cerrada del todo
        bool solid = slide <= 0.001f;
        if (leafColliders != null)
            foreach (var c in leafColliders) if (c != null && c.enabled != solid) c.enabled = solid;
    }

    // ---------------------------------------------------------------
    // Manija giratoria
    // ---------------------------------------------------------------
    void SetupHandle(Bounds b)
    {
        Vector3 edgeA = b.center - widthAxis * halfWidth, edgeB = b.center + widthAxis * halfWidth;
        Vector3 hingeEdge, freeEdge;
        switch (hinge)
        {
            case Hinge.BordeIzquierdo: hingeEdge = edgeA; freeEdge = edgeB; break;
            case Hinge.BordeDerecho: hingeEdge = edgeB; freeEdge = edgeA; break;
            default:
                Vector3 c = pairCenter != null ? pairCenter.position : b.center + widthAxis * halfWidth;
                bool aFarther = Flat(edgeA - c).sqrMagnitude >= Flat(edgeB - c).sqrMagnitude;
                hingeEdge = aFarther ? edgeA : edgeB; freeEdge = aFarther ? edgeB : edgeA;
                break;
        }
        hingePoint = new Vector3(hingeEdge.x, b.min.y, hingeEdge.z);

        float h = Mathf.Min(handleHeight, b.size.y * 0.9f);
        Vector3 handlePos = Vector3.Lerp(freeEdge, hingeEdge, handleInset);
        handlePos.y = b.min.y + h;

        var go = new GameObject("Manija (agarrar)");
        go.layer = gameObject.layer;
        go.transform.position = handlePos;
        go.transform.SetParent(transform, true);
        var sphere = go.AddComponent<SphereCollider>();
        sphere.radius = handleRadius / Mathf.Max(1e-4f, MaxAbs(go.transform.lossyScale));
        var interactable = go.AddComponent<XRSimpleInteractable>();
        interactable.selectEntered.AddListener(OnHandleGrabbed);
        handle = go.transform;
    }

    void OnHandleGrabbed(SelectEnterEventArgs args)
    {
        var input = args.interactorObject.transform.GetComponentInParent<XRBaseInputInteractor>();
        if (input != null) input.SendHapticImpulse(hapticAmplitude, 0.08f);
        AlternarPuerta();
    }

    /// Abre o cierra a mano (también se puede llamar desde un botón o un UnityEvent)
    public void AlternarPuerta()
    {
        if (modo == Modo.AutomaticaDeslizante) { IsOpen = !IsOpen; return; }

        IsOpen = !IsOpen;
        if (IsOpen && openAwayFromPlayer && Camera.main != null)
        {
            float playerSide = Mathf.Sign(Vector3.Dot(Flat(Camera.main.transform.position - closedPos), doorNormal));
            Vector3 freeDir = Flat(handle != null ? handle.position - hingePoint : closedPos - hingePoint).normalized;
            float moveSide = Mathf.Sign(Vector3.Dot(Vector3.Cross(Vector3.up, freeDir), doorNormal));
            side = -playerSide * moveSide;
            if (side == 0f) side = 1f;
        }
        target = IsOpen ? openAngle * side : 0f;
        if (sound != null) sound.Play();
    }

    public void Abrir() { if (!IsOpen) AlternarPuerta(); }
    public void Cerrar() { if (IsOpen) AlternarPuerta(); }

    void Update()
    {
        if (modo == Modo.AutomaticaDeslizante) { UpdateSliding(); return; }

        if (Mathf.Approximately(angle, target)) return;
        angle = Mathf.MoveTowards(angle, target, speed * Time.deltaTime);
        var rot = Quaternion.AngleAxis(angle, Vector3.up);
        transform.SetPositionAndRotation(hingePoint + rot * (closedPos - hingePoint), rot * closedRot);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static float MaxAbs(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 c = pairCenter != null ? pairCenter.position : transform.position;
        Gizmos.DrawWireSphere(new Vector3(c.x, transform.position.y, c.z), openDistance);
    }
}
