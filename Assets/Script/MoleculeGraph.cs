using System;
using System.Collections.Generic;
using System.Linq;

/// Grafo de una molécula: átomos = nodos, enlaces = aristas con orden (1, 2, 3).
/// Se puede construir desde átomos reales de la escena (FromAtom) o desde un RecipeData.
public class MoleculeGraph
{
    public readonly List<string> symbols = new List<string>();
    public readonly List<int> valences = new List<int>();
    public readonly List<(int a, int b, int order)> edges = new List<(int, int, int)>();

    public int AtomCount => symbols.Count;

    public int AddAtom(string symbol, int valence)
    {
        symbols.Add(symbol); valences.Add(valence);
        return symbols.Count - 1;
    }

    public void AddEdge(int a, int b, int order) => edges.Add((a, b, order));

    // ---------------------------------------------------------------
    // Construcción desde la escena
    // ---------------------------------------------------------------
    public static MoleculeGraph FromAtom(Atom start, out List<Atom> atoms)
    {
        atoms = new List<Atom>();
        var g = new MoleculeGraph();
        if (start == null) return g;

        var index = new Dictionary<Atom, int>();
        var bars = new HashSet<Bond>();
        var queue = new Queue<Atom>();
        queue.Enqueue(start);

        // 1) Recorrido: encontrar todos los átomos y barras conectados
        while (queue.Count > 0)
        {
            var a = queue.Dequeue();
            if (index.ContainsKey(a)) continue;
            index[a] = g.AddAtom(a.data.symbol, a.data.valence);
            atoms.Add(a);

            foreach (var s in a.Sockets)
                if (s != null && s.Held != null) queue.Enqueue(s.Held);
            if (a.HeldBy != null) queue.Enqueue(a.HeldBy.Owner);

            foreach (var b in a.Bars)
            {
                bars.Add(b);
                if (b.AtomA != null) queue.Enqueue(b.AtomA);
                if (b.AtomB != null) queue.Enqueue(b.AtomB);
            }
        }

        // 2) Aristas: enlaces directos (orden 1, contados desde el dueño del socket)
        foreach (var a in atoms)
            foreach (var s in a.Sockets)
                if (s != null && s.Held != null && index.ContainsKey(s.Held))
                    g.AddEdge(index[a], index[s.Held], 1);

        // 3) Aristas: barras completas con su orden
        foreach (var b in bars)
            if (b.IsComplete && index.ContainsKey(b.AtomA) && index.ContainsKey(b.AtomB))
                g.AddEdge(index[b.AtomA], index[b.AtomB], 1);

        return g;
    }

    // ---------------------------------------------------------------
    // Consultas
    // ---------------------------------------------------------------
    public int UsedValence(int i)
    {
        int sum = 0;
        foreach (var e in edges)
            if (e.a == i || e.b == i) sum += e.order;
        return sum;
    }

    public int OpenValences
    {
        get { int open = 0; for (int i = 0; i < AtomCount; i++) open += valences[i] - UsedValence(i); return open; }
    }

    public Dictionary<string, int> AtomCounts()
    {
        var d = new Dictionary<string, int>();
        foreach (var s in symbols) d[s] = d.TryGetValue(s, out var n) ? n + 1 : 1;
        return d;
    }

    public int[] BarCounts()   // índice 0 = simples, 1 = dobles, 2 = triples
    {
        var c = new int[3];
        foreach (var e in edges) c[Math.Clamp(e.order, 1, 3) - 1]++;
        return c;
    }

    public bool IsConnected()
    {
        if (AtomCount == 0) return false;
        var seen = new HashSet<int> { 0 };
        var stack = new Stack<int>(); stack.Push(0);
        while (stack.Count > 0)
        {
            int cur = stack.Pop();
            foreach (var e in edges)
            {
                int other = e.a == cur ? e.b : e.b == cur ? e.a : -1;
                if (other >= 0 && seen.Add(other)) stack.Push(other);
            }
        }
        return seen.Count == AtomCount;
    }

    // ---------------------------------------------------------------
    // Hash canónico por refinamiento iterativo (exacto para grafos acíclicos)
    // ---------------------------------------------------------------
    const ulong FnvOffset = 14695981039346656037UL;
    const ulong FnvPrime = 1099511628211UL;

    static ulong Mix(ulong h, ulong x) { h ^= x; h *= FnvPrime; h ^= h >> 29; return h; }

    static ulong HashString(string s)
    {
        ulong h = FnvOffset;
        foreach (char c in s) { h ^= c; h *= FnvPrime; }
        return h;
    }

    public ulong CanonicalHash()
    {
        int n = AtomCount;
        if (n == 0) return 0;

        var adj = new List<(int nb, int ord)>[n];
        for (int i = 0; i < n; i++) adj[i] = new List<(int, int)>();
        foreach (var e in edges) { adj[e.a].Add((e.b, e.order)); adj[e.b].Add((e.a, e.order)); }

        var label = new ulong[n];
        for (int i = 0; i < n; i++) label[i] = HashString(symbols[i]);

        // n rondas bastan: en un árbol el diámetro es < n
        for (int round = 0; round < n; round++)
        {
            var next = new ulong[n];
            for (int i = 0; i < n; i++)
            {
                var neigh = adj[i].Select(t => Mix(label[t.nb], (ulong)t.ord)).OrderBy(x => x).ToList();
                ulong h = Mix(FnvOffset, label[i]);
                foreach (var x in neigh) h = Mix(h, x);
                next[i] = h;
            }
            label = next;
        }

        ulong result = Mix(FnvOffset, (ulong)n);
        foreach (var x in label.OrderBy(x => x)) result = Mix(result, x);
        return result;
    }
}
