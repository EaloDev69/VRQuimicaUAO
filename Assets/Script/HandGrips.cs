using UnityEngine;
using UnityEngine.XR;

/// Lee los gatillos laterales (grips) de las dos manos, sin depender del Input System ni del SDK de Meta.
///   Left  = grip izquierdo  = OVRInput.Axis1D.PrimaryHandTrigger
///   Right = grip derecho    = OVRInput.Axis1D.SecondaryHandTrigger
/// Funciona con OpenXR / Oculus mediante el sistema XR de Unity (UnityEngine.XR.InputDevices).
public static class HandGrips
{
    public static float Left => Read(XRNode.LeftHand);
    public static float Right => Read(XRNode.RightHand);

    /// ¿Están pulsados los dos grips a la vez?
    public static bool BothPressed(float threshold = 0.6f) => Left >= threshold && Right >= threshold;

    static float Read(XRNode node)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        return device.isValid && device.TryGetFeatureValue(CommonUsages.grip, out float value) ? value : 0f;
    }
}
