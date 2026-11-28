using System.Collections.Generic;
using UnityEngine;

/// Arma una molécula completa al dar Play, con átomos y barras de verdad (las mismas piezas del juego),
/// para probar el transmutador sin tener que construirla a mano.
/// Uso: arrastra el prefab "H2O_Armado" (o este componente con otra receta) a la escena, encima de una mesa.
/// La molécula se arma en la posición del objeto y después este objeto se borra.
public class MoleculaDePrueba : MonoBehaviour
{
    [Tooltip("Receta a armar (H2O, CH4, NH3...). Usa sus átomos y enlaces")]
    public RecipeData recipe;
    [Tooltip("Prefab genérico del átomo (AtomBase)")]
    public Atom atomPrefab;
    [Tooltip("Prefab de la barra (BondBar)")]
    public Bond barPrefab;
    [Tooltip("Borrar este objeto al terminar de armar")]
    public bool destroyAfterBuild = true;

    void Start()
    {
        if (recipe == null || atomPrefab == null || barPrefab == null || recipe.atoms == null || recipe.atoms.Length == 0)
        {
            Debug.LogError("[MoleculaDePrueba] Falta la receta, el prefab del átomo o el de la barra.", this);
            return;
        }
        Build();
        if (destroyAfterBuild) Destroy(gameObject);
    }

    void Build()
    {
        // Padre inactivo: así el AtomData se asigna antes del Awake del átomo (igual que el AtomSpawner)
        var holder = new GameObject("holder");
        holder.SetActive(false);

        var atoms = new Atom[recipe.atoms.Length];
        atoms[0] = SpawnAtom(recipe.atoms[0], transform.position, holder.transform);

        // Enlaces en orden de recorrido desde el átomo 0 (las recetas son árboles)
        var pending = new List<RecipeData.BondDef>(recipe.bonds ?? new RecipeData.BondDef[0]);
        bool progress = true;
        while (pending.Count > 0 && progress)
        {
            progress = false;
            for (int i = 0; i < pending.Count; i++)
            {
                var bd = pending[i];
                int from = atoms[bd.a] != null ? bd.a : atoms[bd.b] != null ? bd.b : -1;
                if (from < 0) continue;
                int to = from == bd.a ? bd.b : bd.a;
                pending.RemoveAt(i); i--; progress = true;
                if (atoms[to] != null) continue;                         // ya existe (anillo): se omite
                atoms[to] = Link(atoms[from], recipe.atoms[to], holder.transform);
                if (atoms[to] == null) Debug.LogWarning($"[MoleculaDePrueba] No pude unir el átomo {to}.", this);
            }
        }

        Destroy(holder);
        Debug.Log($"[MoleculaDePrueba] {recipe.displayName} armada con {atoms.Length} átomos.", this);
    }

    Atom SpawnAtom(AtomData data, Vector3 pos, Transform holder)
    {
        var a = Instantiate(atomPrefab, pos, Quaternion.identity, holder);
        a.data = data;
        a.name = data.symbol + " (prueba)";
        a.transform.SetParent(null, true);                               // aquí corre su Awake con el dato ya puesto
        return a;
    }

    // Pone una barra saliendo de un puerto libre de 'from' y un átomo nuevo en la otra punta
    Atom Link(Atom from, AtomData data, Transform holder)
    {
        BarPort port = null;
        foreach (var p in from.Ports) if (p != null && p.IsFree) { port = p; break; }
        if (port == null) return null;

        Vector3 dir = port.transform.position - from.transform.position;
        if (dir.sqrMagnitude < 1e-8f) dir = Random.onUnitSphere;
        dir.Normalize();

        var bar = Instantiate(barPrefab, from.transform.position + dir * 0.15f, Quaternion.identity);
        // Eje largo de la barra: de PlugB a PlugA. Se orienta para que PlugA mire al átomo de origen.
        Vector3 axisLocal = (bar.PlugA.transform.localPosition - bar.PlugB.transform.localPosition).normalized;
        bar.transform.rotation = Quaternion.FromToRotation(bar.transform.TransformDirection(axisLocal), -dir) * bar.transform.rotation;
        float half = (bar.PlugA.transform.position - bar.PlugB.transform.position).magnitude * 0.5f;
        bar.transform.position += (from.transform.position + dir * (from.atomRadius + half)) - BarCenter(bar);
        Physics.SyncTransforms();

        if (!bar.TryAttach(bar.PlugA, port)) { Destroy(bar.gameObject); return null; }

        Vector3 farTip = bar.PlugB.transform.position;
        var to = SpawnAtom(data, farTip + dir * atomPrefab.atomRadius, holder);
        Physics.SyncTransforms();
        BarPort toPort = null;
        foreach (var p in to.Ports) if (p != null && p.IsFree) { toPort = p; break; }
        if (toPort == null || !bar.TryAttach(bar.PlugB, toPort)) return to;
        return to;
    }

    static Vector3 BarCenter(Bond bar) => (bar.PlugA.transform.position + bar.PlugB.transform.position) * 0.5f;
}
