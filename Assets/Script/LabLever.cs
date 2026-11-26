using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// Palanca de laboratorio con tres posiciones: Baja, Media y Alta (presión o temperatura).
/// Se agarra y se inclina con la mano; al soltarla se acomoda en la posición más cercana.
/// Montaje: objeto raíz con este script -> hijo "Pivot" (se gira sobre su eje X local)
///          -> hijo con la malla de la palanca y un collider (la manija).
public class LabLever : XRSimpleInteractable
{
    [Header("Palanca")]
    public Transform pivot;                      // lo que gira
    public float lowAngle = -40f;
    public float mediumAngle = 0f;
    public float highAngle = 40f;                // si la palanca gira al revés, cambia los signos
    public LeverLevel startLevel = LeverLevel.Low;
    public float snapSpeed = 12f;

    [Header("Feedback")]
    public AudioSource clickSound;               // clic al cambiar de posición
    [Range(0f, 1f)] public float hapticAmplitude = 0.3f;
    public float hapticDuration = 0.05f;
    public UnityEvent<LeverLevel> onLevelChanged;

    public event Action<LeverLevel> LevelChanged;
    public LeverLevel Level { get; private set; }

    Quaternion restRotation = Quaternion.identity;
    float currentAngle;

    protected override void Awake()
    {
        base.Awake();
        if (pivot == null) { Debug.LogError("[LabLever] Falta asignar el Pivot.", this); return; }
        restRotation = pivot.localRotation;
        Level = startLevel;
        currentAngle = AngleOf(Level);
        Apply();
    }

    void Update()
    {
        if (pivot == null) return;

        if (isSelected && interactorsSelecting.Count > 0)
        {
            // Sigue a la mano, limitado al recorrido de la palanca
            var attach = interactorsSelecting[0].GetAttachTransform(this);
            float target = AngleFromHand(attach.position);
            currentAngle = Mathf.Clamp(target, Mathf.Min(lowAngle, highAngle), Mathf.Max(lowAngle, highAngle));

            var nearest = NearestLevel(currentAngle);
            if (nearest != Level) ChangeLevel(nearest, true);
        }
        else
        {
            // Suelta: se acomoda en la posición actual
            currentAngle = Mathf.Lerp(currentAngle, AngleOf(Level), 1f - Mathf.Exp(-snapSpeed * Time.deltaTime));
        }
        Apply();
    }

    /// Para fijar la posición por código (reinicios, pruebas)
    public void SetLevel(LeverLevel level)
    {
        if (level != Level) ChangeLevel(level, false);
        currentAngle = AngleOf(level);
        Apply();
    }

    // ---------------------------------------------------------------
    void ChangeLevel(LeverLevel level, bool haptic)
    {
        Level = level;
        if (clickSound != null) clickSound.Play();

        if (haptic && interactorsSelecting.Count > 0)
        {
            var input = interactorsSelecting[0].transform.GetComponentInParent<XRBaseInputInteractor>();
            if (input != null) input.SendHapticImpulse(hapticAmplitude, hapticDuration);
        }

        LevelChanged?.Invoke(level);
        onLevelChanged.Invoke(level);
    }

    float AngleOf(LeverLevel l)
    {
        switch (l)
        {
            case LeverLevel.Low: return lowAngle;
            case LeverLevel.High: return highAngle;
            default: return mediumAngle;
        }
    }

    LeverLevel NearestLevel(float angle)
    {
        var best = LeverLevel.Medium; float bestD = float.MaxValue;
        foreach (LeverLevel l in Enum.GetValues(typeof(LeverLevel)))
        {
            float d = Mathf.Abs(angle - AngleOf(l));
            if (d < bestD) { bestD = d; best = l; }
        }
        return best;
    }

    // Ángulo de la mano alrededor del eje X del pivote (0 = palanca derecha)
    float AngleFromHand(Vector3 worldPos)
    {
        var parent = pivot.parent;
        Vector3 local = parent != null ? parent.InverseTransformPoint(worldPos) : worldPos;
        Vector3 v = Quaternion.Inverse(restRotation) * (local - pivot.localPosition);
        return Mathf.Atan2(v.z, v.y) * Mathf.Rad2Deg;
    }

    void Apply() { pivot.localRotation = restRotation * Quaternion.Euler(currentAngle, 0f, 0f); }
}
