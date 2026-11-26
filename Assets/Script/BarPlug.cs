using UnityEngine;

public class BarPlug : MonoBehaviour
{
    [HideInInspector] public Bond bond;
    [HideInInspector] public bool needsLeave;   // tras desenganchar: debe alejarse antes de reenganchar
    public BarPort Port;
    public Joint Joint;
    public bool IsFree => Port == null;

    public static BarPlug FindNearestFree(Vector3 pos, float radius)
    {
        BarPlug best = null; float bestD = float.MaxValue;
        foreach (var c in Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Collide))
        {
            var p = c.GetComponent<BarPlug>();
            if (p == null || !p.IsFree) continue;
            float d = (p.transform.position - pos).sqrMagnitude;
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }
}