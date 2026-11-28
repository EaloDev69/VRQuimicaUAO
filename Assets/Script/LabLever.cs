using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// Palanca de laboratorio con tres posiciones: Baja, Media y Alta (presión o temperatura).
/// Se agarra y se mueve con la mano DE ARRIBA A ABAJO; al soltarla se acomoda en la posición más cercana.
/// Montaje: objeto raíz con este script -> hijo "Pivot" (lo que gira) -> hijo con la malla y un collider (la manija).
///
/// Eje de giro:
///   - Auto (recomendado): se calcula solo al empezar como el eje horizontal perpendicular a la manija,
///     así la manija sube y baja sin importar cómo esté orientado el modelo.
///   - X / Y / Z: eje local fijo del padre del Pivot, por si algún modelo necesita otro.
/// Ángulos: positivo = manija hacia ARRIBA, negativo = hacia ABAJO (con eje Auto).
public class LabLever : XRSimpleInteractable
{
    public enum AxisMode { Auto, X, Y, Z }

    [Header("Palanca")]
    public Transform pivot;                      // lo que gira
    [Tooltip("Auto = la manija sube y baja. X/Y/Z = eje local fijo del padre del Pivot")]
    public AxisMode axisMode = AxisMode.Auto;
    public float lowAngle = -40f;                // abajo
    public float mediumAngle = 0f;
    public float highAngle = 40f;                // arriba
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
    Vector3 axisParent = Vector3.right;          // eje de giro en el espacio del padre del Pivot
    Vector3 restDirParent = Vector3.forward;     // dirección pivote -> manija en reposo (espacio del padre)
    float currentAngle;
    float grabOffset;                            // diferencia entre el ángulo de la mano y el de la palanca al agarrar
    bool wasSelected;

    protected override void Awake()
    {
        // Entradas vacías en la lista Colliders impiden que XRI busque los colliders solos: se limpian
        colliders.RemoveAll(c => c == null);
        base.Awake();
        if (pivot == null) { Debug.LogError("[LabLever] Falta asignar el Pivot.", this); return; }
        restRotation = pivot.localRotation;
        ComputeAxis();
        Level = startLevel;
        currentAngle = AngleOf(Level);
        Apply();
    }

    // Eje horizontal perpendicular a la manija: girar alrededor de él mueve la manija arriba/abajo
    void ComputeAxis()
    {
        var parent = pivot.parent;
        Vector3 handlePoint = pivot.position + pivot.forward * 0.1f;
        var handle = FindHandleCollider();
        if (handle != null) handlePoint = handle.bounds.center;

        Vector3 dirWorld = handlePoint - pivot.position;
        if (dirWorld.sqrMagnitude < 1e-8f) dirWorld = pivot.forward;
        restDirParent = parent != null ? parent.InverseTransformDirection(dirWorld).normalized : dirWorld.normalized;

        switch (axisMode)
        {
            case AxisMode.X: axisParent = Vector3.right; break;
            case AxisMode.Y: axisParent = Vector3.up; break;
            case AxisMode.Z: axisParent = Vector3.forward; break;
            default:
                Vector3 upParent = parent != null ? parent.InverseTransformDirection(Vector3.up) : Vector3.up;
                Vector3 a = Vector3.Cross(restDirParent, upParent);
                // manija casi vertical: se usa el eje "derecha" del padre (inclina hacia adelante/atrás)
                axisParent = a.sqrMagnitude > 0.01f ? a.normalized : Vector3.right;
                break;
        }
    }

    Collider FindHandleCollider()
    {
        foreach (var c in pivot.GetComponentsInChildren<Collider>())
            if (c.enabled && !c.isTrigger) return c;
        var any = pivot.GetComponentInChildren<Collider>();
        return any;
    }

    void Update()
    {
        if (pivot == null) return;

        if (isSelected && interactorsSelecting.Count > 0)
        {
            var attach = interactorsSelecting[0].GetAttachTransform(this);
            float hand = AngleFromHand(attach.position);
            if (!wasSelected) { grabOffset = currentAngle - hand; wasSelected = true; }   // no salta al agarrar

            float target = hand + grabOffset;
            currentAngle = Mathf.Clamp(target, Mathf.Min(lowAngle, highAngle), Mathf.Max(lowAngle, highAngle));

            var nearest = NearestLevel(currentAngle);
            if (nearest != Level) ChangeLevel(nearest, true);
        }
        else
        {
            wasSelected = false;
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

    // Ángulo de la mano alrededor del eje de giro, medido desde la posición de reposo de la manija
    float AngleFromHand(Vector3 worldPos)
    {
        var parent = pivot.parent;
        Vector3 local = parent != null ? parent.InverseTransformPoint(worldPos) : worldPos;
        Vector3 v = Vector3.ProjectOnPlane(local - pivot.localPosition, axisParent);
        Vector3 rest = Vector3.ProjectOnPlane(restDirParent, axisParent);
        if (v.sqrMagnitude < 1e-8f || rest.sqrMagnitude < 1e-8f) return currentAngle;
        return Vector3.SignedAngle(rest, v, axisParent);
    }

    void Apply() { pivot.localRotation = Quaternion.AngleAxis(currentAngle, axisParent) * restRotation; }

    void OnDrawGizmosSelected()
    {
        if (pivot == null) return;
        var parent = pivot.parent;
        Vector3 axisW = parent != null ? parent.TransformDirection(axisParent) : axisParent;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(pivot.position - axisW * 0.1f, pivot.position + axisW * 0.1f);
    }
}
