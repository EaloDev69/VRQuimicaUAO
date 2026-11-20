using System;
using System.Collections.Generic;
using UnityEngine;

public enum CompoundCategory { Gas, Liquid, Solid }

[CreateAssetMenu(menuName = "Molecula/Recipe")]
public class RecipeData : ScriptableObject
{
    [Serializable]
    public struct BondDef
    {
        public int a;          // índice en el arreglo "atoms"
        public int b;
    }

    [Header("Identidad")]
    public string displayName;          // "Agua"
    public string formula;              // "H₂O"
    [Range(1, 3)] public int level = 1;
    public CompoundCategory category;

    [Header("Grafo objetivo")]
    [Tooltip("Un elemento por átomo de la molécula. El orden define los índices de 'bonds'.")]
    public AtomData[] atoms;
    public BondDef[] bonds;

    [Header("Juego")]
    public int basePoints = 100;
    [Tooltip("Tiempo límite del pedido en segundos (propuesta, se ajusta en las pruebas)")]
    public float timeLimit = 120f;
    public GameObject effectPrefab;     // efecto de la máquina (agua, llama, etc.)
    public Color sampleColor = Color.white;
    [TextArea] public string educationalFact;

    [NonSerialized] MoleculeGraph cachedGraph;
    [NonSerialized] ulong cachedHash;

    void OnValidate() { cachedGraph = null; }

    /// Para el generador del editor: descarta el grafo cacheado tras cambiar atoms/bonds por código
    public void InvalidateCache() { cachedGraph = null; }

    public MoleculeGraph Graph
    {
        get
        {
            if (cachedGraph != null) return cachedGraph;
            var g = new MoleculeGraph();
            foreach (var a in atoms) g.AddAtom(a.symbol, a.valence);
            foreach (var b in bonds) g.AddEdge(b.a, b.b, 1);
            cachedGraph = g;
            cachedHash = g.CanonicalHash();
            return g;
        }
    }

    public ulong Hash { get { var _ = Graph; return cachedHash; } }

    // Lista de piezas calculada desde el grafo: muñeca, spawner y validación nunca se desincronizan
    public Dictionary<string, int> AtomCounts => Graph.AtomCounts();
    public int[] BarCounts => Graph.BarCounts();   // [barras]

    /// Comprueba que la receta esté bien definida (valencias cerradas y conectada).
    [ContextMenu("Comprobar receta")]
    void Check()
    {
        var g = Graph;
        Debug.Log($"{displayName} {formula}: {g.AtomCount} átomos, barras = " +
                  $"{BarCounts[0]}, valencias abiertas = {g.OpenValences}, " +
                  $"conectada = {g.IsConnected()}", this);
    }
}