using UnityEngine;

/// El letrero gira para que siempre se lea desde donde está el jugador
public class FaceCamera : MonoBehaviour
{
    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var dir = transform.position - cam.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(dir);
    }
}
