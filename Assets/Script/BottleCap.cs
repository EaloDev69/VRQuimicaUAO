using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;   // XRI 2.x: borrar esta línea

/// Tapa de la botella, con configuración automática.
/// USO: pon el modelo de la tapa en la boca de la botella (como hijo de la botella) y añade este script.
/// Al añadirlo (o con clic derecho en el componente -> "Configurar automáticamente") hace solo:
///   - crea el tag "BottleCap" si no existe y se lo pone a la tapa
///   - añade Rigidbody, Collider y XRGrabInteractable a la tapa
///   - crea en la botella un hijo "CapSocket" con un FilteredSocket (filtro por tag) justo donde está la tapa
/// Al dar Play, la tapa encaja sola en el socket. La Evaluadora suma el bonus por tapa si está puesta.
public class BottleCap : MonoBehaviour
{
    public const string TagName = "BottleCap";

    [Tooltip("La botella que lleva esta tapa. Si la tapa es hija de la botella se encuentra sola")]
    public SampleItem bottle;

    [Tooltip("Radio del trigger del socket de la tapa, en metros")]
    public float socketRadius = 0.03f;

    [Tooltip("Suelta la tapa de la botella al empezar (evita dos Rigidbody anidados); el socket la vuelve a sujetar")]
    public bool unparentOnStart = true;

    void Awake()
    {
        if (unparentOnStart && transform.parent != null)
            transform.SetParent(null, true);
    }

#if UNITY_EDITOR
    // Se ejecuta al añadir el componente en el editor
    void Reset() { AutoConfigure(); }

    [ContextMenu("Configurar automáticamente")]
    void AutoConfigure()
    {
        // 1) Tag
        EnsureTag(TagName);
        gameObject.tag = TagName;

        // 2) Física y agarre de la tapa
        if (!TryGetComponent<Rigidbody>(out var rb)) rb = gameObject.AddComponent<Rigidbody>();
        rb.mass = 0.02f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        if (GetComponent<Collider>() == null) gameObject.AddComponent<BoxCollider>();   // se ajusta solo a la malla
        if (GetComponent<XRGrabInteractable>() == null) gameObject.AddComponent<XRGrabInteractable>();

        // 3) Socket de la tapa en la botella
        if (bottle == null) bottle = GetComponentInParent<SampleItem>();
        if (bottle != null) CreateCapSocket(bottle);
        else Debug.LogWarning("[BottleCap] No encontré la botella. Pon la tapa como hija de la botella o arrastra la botella al campo Bottle y usa Configurar automáticamente.", this);

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.EditorUtility.SetDirty(gameObject);
    }

    void CreateCapSocket(SampleItem b)
    {
        // Si ya hay un socket de tapa en la botella, no se duplica
        foreach (var s in b.GetComponentsInChildren<FilteredSocket>(true))
            if (s.filter == SocketFilter.Tag && s.requiredTag == TagName) return;

        var go = new GameObject("CapSocket");
        go.layer = b.gameObject.layer;
        go.transform.SetParent(b.transform, false);
        go.transform.SetPositionAndRotation(transform.position, transform.rotation);   // justo donde está la tapa

        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = socketRadius;

        var socket = go.AddComponent<FilteredSocket>();
        socket.filter = SocketFilter.Tag;
        socket.requiredTag = TagName;
        socket.showInteractableHoverMeshes = false;

        UnityEditor.EditorUtility.SetDirty(b.gameObject);
    }

    // Crea el tag en ProjectSettings/TagManager si todavía no existe
    static void EnsureTag(string tag)
    {
        var asset = UnityEditor.AssetDatabase.LoadMainAssetAtPath("ProjectSettings/TagManager.asset");
        if (asset == null) return;

        var so = new UnityEditor.SerializedObject(asset);
        var tags = so.FindProperty("tags");
        for (int i = 0; i < tags.arraySize; i++)
            if (tags.GetArrayElementAtIndex(i).stringValue == tag) return;

        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
        so.ApplyModifiedProperties();
    }
#endif
}
