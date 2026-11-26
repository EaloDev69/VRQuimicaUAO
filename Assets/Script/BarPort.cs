using UnityEngine;

public class BarPort : MonoBehaviour
{
    public Atom Owner;
    public BarPlug Plug;                 // extremo conectado (null = libre)
    public bool IsFree => Plug == null;

    // Busca un átomo con valencia libre cerca de 'pos' y devuelve uno de sus puertos libres.
    // 'exclude' = átomo que no se debe usar (el del otro extremo de la barra).
    public static BarPort FindFor(Vector3 pos, float radius, Atom exclude = null)
    {
        Atom bestAtom = null; float bestD = float.MaxValue;

        foreach (var c in Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            var atom = c.GetComponentInParent<Atom>();
            if (atom == null || atom == exclude || !atom.HasFreeValence) continue;
            float d = (atom.transform.position - pos).sqrMagnitude;
            if (d < bestD) { bestD = d; bestAtom = atom; }
        }
        if (bestAtom == null) return null;

        foreach (var p in bestAtom.Ports)
            if (p != null && p.IsFree) return p;

        return null;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = IsFree ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.015f);
    }
}