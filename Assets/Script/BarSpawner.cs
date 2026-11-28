using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// Máquina spawner de barras (GDD 4.4), sin script en la cesta.
/// La cesta es una cesta física normal con el tag "Basket". El spawner solo entrega si la detecta en su "zona de cesta"
/// (un BoxCollider que cubre el interior de la cesta cuando está colocada bajo la boquilla).
/// Botones: Spawn -> BarSpawner.Spawn, Borrar -> BarSpawner.DeleteBars (con LabButton).
public class BarSpawner : MonoBehaviour
{
    [Header("Datos")]
    public Bond barPrefab;                  // prefab de la barra simple

    [Header("Boquilla y cesta")]
    public Transform nozzle;                // punto de salida, sobre la apertura de la cesta
    public Collider basketZone;             // BoxCollider o SphereCollider bajo la boquilla. No hace falta que sea trigger
    [Tooltip("Tag que debe tener la cesta (en el objeto con el Rigidbody). El spawner solo funciona si la detecta encima")]
    public string basketTag = "Basket";

    [Header("Reglas")]
    public int maxPieces = 10;              // tope de barras en la cesta (lo ajusta el nivel)
    public int deletePerPress = 1;

    [Header("Contador (opcional)")]
    public TMP_Text countText;              // texto en el panel del spawner

    [Header("Feedback")]
    public UnityEvent onSpawned;
    public UnityEvent onNoBasket;
    public UnityEvent onFull;
    public UnityEvent onDeleted;
    public UnityEvent onNothingToDelete;

    float nextCountUpdate;

    public void SetMaxPieces(int value) => maxPieces = Mathf.Max(0, value);

    // ---------------------------------------------------------------
    public void Spawn()
    {
        if (!BasketPresent()) { onNoBasket.Invoke(); return; }
        if (BarsInZone().Count >= maxPieces) { onFull.Invoke(); return; }

        Instantiate(barPrefab, nozzle.position, Random.rotation);   // cae por su peso dentro de la cesta
        onSpawned.Invoke();
    }

    public void DeleteBars()
    {
        if (!BasketPresent()) { onNoBasket.Invoke(); return; }

        // Solo barras sueltas dentro de la zona; nunca las que estén en la mano o enlazadas
        var candidates = BarsInZone()
            .Where(b => b.PlugA.IsFree && b.PlugB.IsFree && !IsHeld(b))
            .OrderByDescending(b => b.transform.position.y)         // las de arriba, que son las más recientes
            .Take(deletePerPress).ToList();

        if (candidates.Count == 0) { onNothingToDelete.Invoke(); return; }
        foreach (var b in candidates) Destroy(b.gameObject);
        onDeleted.Invoke();
    }

    // ---------------------------------------------------------------
    void Update()
    {
        if (countText == null || Time.time < nextCountUpdate) return;
        nextCountUpdate = Time.time + 0.25f;
        countText.text = BasketPresent() ? $"Barras: {BarsInZone().Count}" : "Sin cesta";
    }

    Collider[] OverlapZone()
    {
        if (basketZone == null) return new Collider[0];
        var t = basketZone.transform;

        if (basketZone is SphereCollider sphere)
        {
            float r = sphere.radius * Mathf.Max(t.lossyScale.x, t.lossyScale.y, t.lossyScale.z);
            return Physics.OverlapSphere(t.TransformPoint(sphere.center), r, ~0, QueryTriggerInteraction.Ignore);
        }
        if (basketZone is BoxCollider box)
        {
            var half = Vector3.Scale(box.size, t.lossyScale) * 0.5f;
            return Physics.OverlapBox(t.TransformPoint(box.center), half, t.rotation, ~0, QueryTriggerInteraction.Ignore);
        }

        Debug.LogWarning("[BarSpawner] basketZone debe ser un BoxCollider o un SphereCollider.", this);
        return new Collider[0];
    }

    // La cesta cuenta como presente si alguno de sus colliders está dentro de la zona
    bool BasketPresent()
    {
        foreach (var c in OverlapZone())
        {
            // El tag va en el objeto raíz de la cesta (el del Rigidbody); los colliders son hijos
            var owner = c.attachedRigidbody != null ? c.attachedRigidbody.gameObject : c.gameObject;
            if (HasTag(owner)) return true;
        }
        return false;
    }

    bool tagWarningShown;

    // La cesta se reconoce por su tag; si el tag no existe o la cesta no lo tiene puesto,
    // se reconoce por el nombre ("Canasta", "Cesta" o "Basket"), para que el spawner no quede bloqueado.
    bool HasTag(GameObject go)
    {
        try { if (go.CompareTag(basketTag)) return true; }
        catch (UnityException)
        {
            if (!tagWarningShown)
            {
                tagWarningShown = true;
                Debug.LogWarning($"[BarSpawner] El tag \"{basketTag}\" no existe; la cesta se reconoce por su nombre (Canasta / Cesta / Basket).", this);
            }
        }
        return LooksLikeBasket(go.transform) || LooksLikeBasket(go.transform.root);
    }

    static bool LooksLikeBasket(Transform t)
    {
        if (t == null) return false;
        string n = t.name.ToLowerInvariant();
        return n.Contains("canasta") || n.Contains("cesta") || n.Contains("basket");
    }

    List<Bond> BarsInZone()
    {
        var set = new HashSet<Bond>();
        foreach (var c in OverlapZone())
        {
            var b = c.GetComponentInParent<Bond>();
            if (b != null) set.Add(b);
        }
        return set.ToList();
    }

    static bool IsHeld(Bond b)
    {
        var g = b.GetComponent<XRGrabInteractable>();
        return g != null && g.isSelected;
    }

    void OnDrawGizmosSelected()
    {
        if (basketZone == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.matrix = basketZone.transform.localToWorldMatrix;
        if (basketZone is BoxCollider box) Gizmos.DrawWireCube(box.center, box.size);
        else if (basketZone is SphereCollider sphere) Gizmos.DrawWireSphere(sphere.center, sphere.radius);
    }
}