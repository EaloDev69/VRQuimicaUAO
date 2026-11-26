<<<<<<< HEAD
using System;
=======
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

<<<<<<< HEAD
/// Máquina spawner de átomos (GDD 4.4).
/// Un botón por elemento deja caer un átomo de ese elemento en la cesta de átomos bajo la boquilla.
/// Un botón "Borrar" deshace el último átomo entregado; opcionalmente cada elemento tiene su botón de borrado.
/// Usa un solo prefab de átomo: el AtomData se asigna al instanciar (antes de que corran los Awake).
public class AtomSpawner : MonoBehaviour
{
    [Serializable]
    public class ElementEntry
    {
        public AtomData data;
        public LabButton spawnButton;      // botón del elemento (se conecta solo, no hay que usar On Pressed)
        public LabButton deleteButton;     // opcional: borra un átomo de ESTE elemento
        public TMP_Text label;             // opcional: se rellena con el símbolo
    }

    [Header("Datos")]
    public Atom atomPrefab;                // prefab genérico: Atom + AtomSocketBuilder + Rigidbody + XRGrabInteractable
    public ElementEntry[] elements;
=======
/// Máquina spawner de átomos (GDD 4.4), con el mismo esquema que BarSpawner.
/// Botones (LabButton), en On Pressed ():
///   - un botón por elemento  -> AtomSpawner.Spawn (AtomData)  y se arrastra el AtomData como parámetro
///   - botón Borrar           -> AtomSpawner.DeleteAtoms
/// Usa un solo prefab de átomo: el AtomData se asigna al instanciar (antes de que corran los Awake).
public class AtomSpawner : MonoBehaviour
{
    [Header("Datos")]
    public Atom atomPrefab;                // prefab genérico: Atom + AtomSocketBuilder + Rigidbody + XRGrabInteractable
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3

    [Header("Boquilla y cesta")]
    public Transform nozzle;
    public Collider basketZone;            // BoxCollider o SphereCollider sobre la cesta
    public string basketTag = "BasketAtoms";

    [Header("Reglas")]
    public int maxPieces = 12;
    public int deletePerPress = 1;

    [Header("Contador (opcional)")]
    public TMP_Text countText;

    [Header("Feedback")]
    public UnityEvent onSpawned;
    public UnityEvent onNoBasket;
    public UnityEvent onFull;
    public UnityEvent onDeleted;
    public UnityEvent onNothingToDelete;

    Transform holder;                                      // padre inactivo para asignar el AtomData antes del Awake
    readonly List<Atom> spawnedOrder = new List<Atom>();   // para "deshacer el último"
    float nextCountUpdate;

    public void SetMaxPieces(int value) => maxPieces = Mathf.Max(0, value);

<<<<<<< HEAD
    // ---------------------------------------------------------------
=======
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
    void Awake()
    {
        var h = new GameObject("AtomSpawnHolder");
        h.SetActive(false);
        h.transform.SetParent(transform, false);
        holder = h.transform;
<<<<<<< HEAD

        foreach (var e in elements)
        {
            if (e == null || e.data == null) continue;
            var entry = e;     // copia para el closure
            if (entry.label != null) entry.label.text = entry.data.symbol;
            if (entry.spawnButton != null) entry.spawnButton.onPressed.AddListener(() => Spawn(entry.data));
            if (entry.deleteButton != null) entry.deleteButton.onPressed.AddListener(() => DeleteElement(entry.data));
        }
    }

    // ---------------------------------------------------------------
    public void Spawn(AtomData data)
    {
        if (data == null) return;
=======
    }

    // ---------------------------------------------------------------
    /// Botón de elemento: On Pressed -> AtomSpawner.Spawn (AtomData), con el AtomData arrastrado en el evento.
    public void Spawn(AtomData data)
    {
        if (data == null) { Debug.LogWarning("[AtomSpawner] Falta el AtomData en el evento On Pressed del botón.", this); return; }
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
        if (!BasketPresent()) { onNoBasket.Invoke(); return; }
        if (AtomsInZone().Count >= maxPieces) { onFull.Invoke(); return; }

        // Se instancia bajo un padre inactivo: Awake no corre hasta sacarlo, así Atom y
        // AtomSocketBuilder ya encuentran el AtomData asignado.
<<<<<<< HEAD
        var atom = Instantiate(atomPrefab, nozzle.position, UnityEngine.Random.rotation, holder);
=======
        var atom = Instantiate(atomPrefab, nozzle.position, Random.rotation, holder);
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
        atom.data = data;
        atom.name = data.symbol;
        atom.transform.SetParent(null, true);

        spawnedOrder.Add(atom);
        onSpawned.Invoke();
    }

<<<<<<< HEAD
    /// Botón "Borrar átomos": deshace el último átomo entregado que siga suelto en la cesta.
=======
    /// Botón Borrar: deshace el último átomo entregado que siga suelto en la cesta.
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
    public void DeleteAtoms()
    {
        if (!BasketPresent()) { onNoBasket.Invoke(); return; }

        var inZone = new HashSet<Atom>(AtomsInZone());
        int removed = 0;

        for (int i = spawnedOrder.Count - 1; i >= 0 && removed < deletePerPress; i--)
        {
            var a = spawnedOrder[i];
            if (a == null) { spawnedOrder.RemoveAt(i); continue; }
            if (!inZone.Contains(a) || !IsLooseAndFree(a)) continue;
            spawnedOrder.RemoveAt(i);
            Destroy(a.gameObject);
            removed++;
        }

        // Respaldo: si no queda ninguno de los entregados, borra el átomo suelto más alto
        if (removed == 0) removed = DestroyHighest(inZone.Where(IsLooseAndFree), deletePerPress);

        if (removed > 0) onDeleted.Invoke(); else onNothingToDelete.Invoke();
    }

<<<<<<< HEAD
    /// Borra un átomo de un elemento concreto (botón de borrado por elemento).
=======
    /// Opcional: borra un átomo de un elemento concreto (botón propio con el AtomData como parámetro).
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
    public void DeleteElement(AtomData data)
    {
        if (!BasketPresent()) { onNoBasket.Invoke(); return; }

        int removed = DestroyHighest(AtomsInZone().Where(a => a.data == data && IsLooseAndFree(a)), deletePerPress);
        if (removed > 0) onDeleted.Invoke(); else onNothingToDelete.Invoke();
    }

    static int DestroyHighest(IEnumerable<Atom> atoms, int count)
    {
        var list = atoms.OrderByDescending(a => a.transform.position.y).Take(count).ToList();
        foreach (var a in list) Destroy(a.gameObject);
        return list.Count;
    }

    // ---------------------------------------------------------------
    void Update()
    {
        if (countText == null || Time.time < nextCountUpdate) return;
        nextCountUpdate = Time.time + 0.25f;

        if (!BasketPresent()) { countText.text = "Sin cesta"; return; }

        var groups = AtomsInZone().GroupBy(a => a.data.symbol).OrderBy(g => g.Key)
                                  .Select(g => $"{g.Key}: {g.Count()}");
        var text = string.Join(" · ", groups);
        countText.text = text.Length == 0 ? "Vacía" : text;
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

        Debug.LogWarning("[AtomSpawner] basketZone debe ser un BoxCollider o un SphereCollider.", this);
        return new Collider[0];
    }

    bool BasketPresent()
    {
        foreach (var c in OverlapZone())
        {
            var owner = c.attachedRigidbody != null ? c.attachedRigidbody.gameObject : c.gameObject;
            if (HasTag(owner)) return true;
        }
        return false;
    }

    bool HasTag(GameObject go)
    {
        try { return go.CompareTag(basketTag); }
        catch (UnityException)
        {
            Debug.LogError($"[AtomSpawner] El tag \"{basketTag}\" no existe. Créalo en Edit > Project Settings > Tags and Layers.", this);
            return false;
        }
    }

    List<Atom> AtomsInZone()
    {
        var set = new HashSet<Atom>();
        foreach (var c in OverlapZone())
        {
            var a = c.GetComponentInParent<Atom>();
            if (a != null && a.data != null) set.Add(a);
        }
        return set.ToList();
    }

    // Átomo suelto: sin enlaces, sin barras a medio poner y sin estar en la mano
    static bool IsLooseAndFree(Atom a)
    {
        if (a.HeldBy != null || a.BondCount > 0) return false;
        if (a.Ports.Any(p => p != null && !p.IsFree)) return false;
        var g = a.GetComponent<XRGrabInteractable>();
        return g == null || !g.isSelected;
    }

    void OnDrawGizmosSelected()
    {
        if (basketZone == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.matrix = basketZone.transform.localToWorldMatrix;
        if (basketZone is BoxCollider box) Gizmos.DrawWireCube(box.center, box.size);
        else if (basketZone is SphereCollider sphere) Gizmos.DrawWireSphere(sphere.center, sphere.radius);
    }
<<<<<<< HEAD
}
=======
}
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
