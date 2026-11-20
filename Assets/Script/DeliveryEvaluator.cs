using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public struct DeliveryResult
{
    public RecipeData recipe;
    public int basePoints;
    public int bonusPoints;
    public int totalPoints;
    public float timeUsed;
}

public enum RejectReason { Empty, NotClassified, NoMatchingOrder }

/// Máquina Evaluadora (GDD 4.10).
/// El jugador deja la muestra (botella o vaso lleno) en la zona de entrada; al soltarla, la máquina:
///   - la rechaza si está vacía, si falta clasificarla (cuando se exige) o si no corresponde al pedido en curso;
///   - si es válida: puntos = base x (1 + maxBonus x tiempo restante / tiempo límite).
/// No vuelve a analizar la molécula: lee la receta que la Transmutadora guardó en la muestra.
public class DeliveryEvaluator : MonoBehaviour
{
    [Header("Datos")]
    public OrderManager orders;

    [Header("Zona de entrada (ranura)")]
    public Collider inputZone;               // BoxCollider o SphereCollider donde se deja la muestra

    [Header("Reglas")]
    [Range(0f, 1f)] public float maxBonus = 0.5f;
    [Tooltip("Exigir que la muestra esté clasificada (decisión pendiente en el GDD)")]
    public bool requireClassification = false;

    [Header("Rechazo")]
    public Vector3 rejectDirection = Vector3.forward;   // en local: hacia dónde rebota la muestra
    public float rejectSpeed = 1.5f;

    [Header("Pantalla de desglose (opcional)")]
    public TMP_Text breakdownText;

    [Header("Feedback")]
    public UnityEvent onDelivered;
    public UnityEvent onRejected;

    public event Action<DeliveryResult> Delivered;
    public event Action<SampleItem, RejectReason> Rejected;

    float nextCheck;

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.2f;

        foreach (var s in SamplesInZone())
        {
            var g = s.GetComponent<XRGrabInteractable>();
            if (g != null && g.isSelected) continue;     // se evalúa cuando el jugador la suelta
            Evaluate(s);
        }
    }

    void Evaluate(SampleItem s)
    {
        if (!s.IsFilled) { Reject(s, RejectReason.Empty); return; }
        if (requireClassification && !s.classified) { Reject(s, RejectReason.NotClassified); return; }

        var order = orders != null ? orders.FindActive(s.recipe) : null;
        if (order == null) { Reject(s, RejectReason.NoMatchingOrder); return; }

        int basePts = s.recipe.basePoints;
        float fraction = order.timeLimit > 0f ? Mathf.Clamp01(order.timeRemaining / order.timeLimit) : 0f;
        int bonus = Mathf.RoundToInt(basePts * maxBonus * fraction);

        var result = new DeliveryResult
        {
            recipe = s.recipe,
            basePoints = basePts,
            bonusPoints = bonus,
            totalPoints = basePts + bonus,
            timeUsed = order.TimeUsed
        };

        orders.Complete(order);
        if (breakdownText != null)
            breakdownText.text = $"{s.recipe.displayName}\nBase: {result.basePoints}\n" +
                                 $"Tiempo: {Mathf.RoundToInt(result.timeUsed)} s\nBonus: +{result.bonusPoints}\n" +
                                 $"Total: {result.totalPoints}";

        Destroy(s.gameObject);                    // la muestra se consume
        onDelivered.Invoke();
        Delivered?.Invoke(result);
    }

    void Reject(SampleItem s, RejectReason reason)
    {
        var rb = s.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.linearVelocity = transform.TransformDirection(rejectDirection).normalized * rejectSpeed + Vector3.up;
        }
        onRejected.Invoke();
        Rejected?.Invoke(s, reason);
    }

    List<SampleItem> SamplesInZone()
    {
        var set = new HashSet<SampleItem>();
        foreach (var c in OverlapZone())
        {
            var s = c.GetComponentInParent<SampleItem>();
            if (s != null) set.Add(s);
        }
        return set.ToList();
    }

    Collider[] OverlapZone()
    {
        if (inputZone == null) return new Collider[0];
        var t = inputZone.transform;

        if (inputZone is SphereCollider sphere)
        {
            float r = sphere.radius * Mathf.Max(t.lossyScale.x, t.lossyScale.y, t.lossyScale.z);
            return Physics.OverlapSphere(t.TransformPoint(sphere.center), r, ~0, QueryTriggerInteraction.Ignore);
        }
        if (inputZone is BoxCollider box)
        {
            var half = Vector3.Scale(box.size, t.lossyScale) * 0.5f;
            return Physics.OverlapBox(t.TransformPoint(box.center), half, t.rotation, ~0, QueryTriggerInteraction.Ignore);
        }

        Debug.LogWarning("[DeliveryEvaluator] inputZone debe ser un BoxCollider o un SphereCollider.", this);
        return new Collider[0];
    }

    void OnDrawGizmosSelected()
    {
        if (inputZone == null) return;
        Gizmos.color = Color.green;
        Gizmos.matrix = inputZone.transform.localToWorldMatrix;
        if (inputZone is BoxCollider box) Gizmos.DrawWireCube(box.center, box.size);
        else if (inputZone is SphereCollider sphere) Gizmos.DrawWireSphere(sphere.center, sphere.radius);
    }
}
