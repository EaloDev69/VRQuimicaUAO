using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;   // XRI 2.x: borrar esta línea

/// Tapa de la botella: se puede quitar con la mano y volver a poner (encaja sola al acercarla a la boca).
/// Va en el objeto de la tapa (hijo de la botella). Al dar Play se configura sola, sin tags:
///   - collider + agarre en la tapa (se agarra desde donde la tocas)
///   - en la botella, un "CapSocket" (FilteredSocket que solo acepta tapas) justo donde está la tapa,
///     con la tapa ya puesta al empezar
///   - la tapa y la botella no chocan entre sí (si no, la tapa empujaría la botella)
/// La Evaluadora suma el bonus por tapa si la botella se entrega con la tapa puesta.
public class BottleCap : MonoBehaviour
{
    public const string TagName = "BottleCap";

    [Tooltip("La botella que lleva esta tapa. Si la tapa es hija de la botella se encuentra sola")]
    public SampleItem bottle;

    [Tooltip("Radio (en metros) de la zona donde la tapa encaja al acercarla a la boca de la botella")]
    public float socketRadius = 0.05f;

    [Tooltip("Suelta la tapa de la botella al empezar (evita dos Rigidbody anidados); el socket la vuelve a sujetar")]
    public bool unparentOnStart = true;

    public FilteredSocket Socket { get; private set; }

    void Awake()
    {
        if (bottle == null) bottle = GetComponentInParent<SampleItem>();
        if (Application.isPlaying && bottle != null) SetupRuntime();
        if (unparentOnStart && transform.parent != null)
            transform.SetParent(null, true);
    }

    void SetupRuntime()
    {
        // 1) Collider de la tapa (se ajusta solo a la malla al añadirlo)
        var col = GetComponent<Collider>();
        if (col == null) col = gameObject.AddComponent<BoxCollider>();

        // 2) Física: dinámica cuando está suelta (el socket la sujeta cuando está puesta)
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.mass = 0.02f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // 3) Agarre: desde donde se toca (el pivote de la malla está lejos de la tapa)
        var grab = GetComponent<XRGrabInteractable>();
        bool hadGrab = grab != null;
        if (!hadGrab) grab = gameObject.AddComponent<XRGrabInteractable>();
        if (!grab.colliders.Contains(col)) grab.colliders.Add(col);
        grab.useDynamicAttach = true;
        grab.interactionLayers = 1;                              // capa Default: el socket de gases (capa Bottle) no la toma
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        if (hadGrab && !grab.enabled) grab.enabled = true;   // al activarlo se registra con sus colliders
        else if (hadGrab) { grab.enabled = false; grab.enabled = true; }

        // 4) Que la tapa no empuje la botella: se hace en Start, cuando la botella ya cambió su collider
        capCollider = col;

        // 5) Socket de la tapa en la botella (si no existe ya)
        foreach (var s in bottle.GetComponentsInChildren<FilteredSocket>(true))
            if (s.filter == SocketFilter.BottleCapOnly || (s.filter == SocketFilter.Tag && s.requiredTag == TagName)) { Socket = s; break; }

        if (Socket == null)
        {
            var r = GetComponent<Renderer>();
            Vector3 mouth = r != null ? r.bounds.center : transform.position;

            var go = new GameObject("CapSocket");
            go.layer = bottle.gameObject.layer;
            go.SetActive(false);                                     // se configura antes de que arranque el socket
            go.transform.SetParent(bottle.transform, false);
            go.transform.position = mouth;

            // Punto de encaje = pose actual de la tapa (así vuelve exactamente a su sitio)
            var attach = new GameObject("CapAttach").transform;
            attach.SetParent(go.transform, false);
            attach.SetPositionAndRotation(transform.position, transform.rotation);

            var sphere = go.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            float scale = Mathf.Max(1e-6f, Mathf.Abs(go.transform.lossyScale.x));
            sphere.radius = socketRadius / scale;                    // radio en metros aunque la botella esté escalada

            Socket = go.AddComponent<FilteredSocket>();
            Socket.filter = SocketFilter.BottleCapOnly;
            Socket.showInteractableHoverMeshes = false;
            Socket.attachTransform = attach;
            Socket.startingSelectedInteractable = grab;              // empieza con la tapa puesta
            go.SetActive(true);
        }
        else if (Socket.startingSelectedInteractable == null)
        {
            Socket.startingSelectedInteractable = grab;
        }
    }

    Collider capCollider;

    void Start()
    {
        if (bottle == null || capCollider == null) return;
        foreach (var bc in bottle.GetComponentsInChildren<Collider>(true))
            if (bc != null && !bc.isTrigger && bc != capCollider) Physics.IgnoreCollision(capCollider, bc, true);
    }

    /// true si la tapa está puesta en su botella
    public bool IsOn => Socket != null && Socket.hasSelection;

#if UNITY_EDITOR
    [ContextMenu("Configurar automáticamente (opcional)")]
    void AutoConfigure()
    {
        if (bottle == null) bottle = GetComponentInParent<SampleItem>();
        if (GetComponent<Collider>() == null) gameObject.AddComponent<BoxCollider>();
        if (GetComponent<Rigidbody>() == null) gameObject.AddComponent<Rigidbody>();
        if (GetComponent<XRGrabInteractable>() == null) gameObject.AddComponent<XRGrabInteractable>();
        UnityEditor.EditorUtility.SetDirty(gameObject);
        Debug.Log("[BottleCap] Listo. El socket de la tapa se crea solo al dar Play.", this);
    }
#endif
}
