using System.Collections.Generic;
using UnityEngine;

public enum ValidationState { Correct, Incomplete, Unstable, Unknown }

public static class MoleculeValidator
{
    /// Valida la molécula a la que pertenece 'anyAtom' contra la lista de recetas.
    /// - Correct:    valencias cerradas y estructura igual a una receta.
    /// - Incomplete: faltan piezas (los átomos caben dentro de alguna receta).
    /// - Unstable:   valencias abiertas y no corresponde a ninguna receta.
    /// - Unknown:    valencias cerradas pero sin receta (candidata a "compuesto curioso").
    public static ValidationState Validate(Atom anyAtom, IEnumerable<RecipeData> recipes,
                                           out RecipeData match, out MoleculeGraph graph)
    {
        match = null;
        graph = MoleculeGraph.FromAtom(anyAtom, out _);
        return Validate(graph, recipes, out match);
    }

    public static ValidationState Validate(MoleculeGraph graph, IEnumerable<RecipeData> recipes,
                                           out RecipeData match)
    {
        match = null;
        if (graph.AtomCount == 0) return ValidationState.Unknown;

        bool closed = graph.OpenValences == 0 && graph.IsConnected();
        ulong hash = graph.CanonicalHash();

        if (closed)
        {
            foreach (var r in recipes)
                if (r != null && r.Hash == hash) { match = r; return ValidationState.Correct; }
            return ValidationState.Unknown;
        }

        // Valencias abiertas: ¿incompleta (le faltan piezas a alguna receta) o inestable?
        var counts = graph.AtomCounts();
        foreach (var r in recipes)
        {
            if (r == null) continue;
            if (FitsInside(counts, r.AtomCounts)) return ValidationState.Incomplete;
        }
        return ValidationState.Unstable;
    }

    static bool FitsInside(Dictionary<string, int> part, Dictionary<string, int> whole)
    {
        foreach (var kv in part)
            if (!whole.TryGetValue(kv.Key, out int n) || kv.Value > n) return false;
        return true;
    }
}

/// Componente de prueba: arrastra un átomo de la molécula y usa clic derecho > "Validar".
public class MoleculeProbe : MonoBehaviour
{
    public Atom target;
    public RecipeData[] recipes;

    [ContextMenu("Validar")]
    public void Run()
    {
        var state = MoleculeValidator.Validate(target, recipes, out var match, out var g);
        Debug.Log($"[Validador] {g.AtomCount} átomos, valencias abiertas {g.OpenValences} → {state}" +
                  (match != null ? $" ({match.formula})" : ""));
    }
}
