using System;
using System.Collections;
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
    public int sealedBonus;
    public int totalPoints;
    public float timeUsed;
}

public enum RejectReason { Empty, NotClassified, NoMatchingOrder, NoActiveOrder, WrongRecipe, Expired }

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
    [Tooltip("Puntos extra si la botella se entrega con la tapa puesta (busca el FilteredSocket de la tapa dentro de la botella)")]
    public int sealedBonusPoints = 5;

    [Header("Rechazo")]
    public Vector3 rejectDirection = Vector3.forward;   // en local: hacia dónde rebota la muestra
    public float rejectSpeed = 1.5f;
    [Tooltip("Segundos que se ignora una muestra después de rechazarla o devolverla (evita rechazos repetidos y sonidos en bucle)")]
    public float rejectCooldown = 1.5f;

    [Header("Contenedores")]
    [Tooltip("Al entregar, el vaso o la botella se vacía y sale por la misma dirección de rechazo para poder reutilizarlo. Desmarcado = se destruye (hace falta otra fuente de contenedores)")]
    public bool recycleContainers = true;

    [Header("Pantalla de desglose (opcional)")]
    public TMP_Text breakdownText;
    [Tooltip("Segundos entre una línea del desglose y la siguiente")]
    public float lineDelay = 0.35f;
    [Tooltip("Segundos que tarda el total en contar de 0 a su valor")]
    public float countDuration = 1.2f;
    public UnityEvent onCountTick;           // un "tic" por línea y mientras cuenta el total
    public UnityEvent onCountFinished;       // campanita final

    [Header("Feedback")]
    public UnityEvent onDelivered;
    public UnityEvent onRejected;

    public event Action<DeliveryResult> Delivered;
    public event Action<SampleItem, RejectReason> Rejected;

    float nextCheck;
    Coroutine countRoutine;
    readonly Dictionary<SampleItem, float> ignoreUntil = new Dictionary<SampleItem, float>();

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.2f;

        foreach (var dead in ignoreUntil.Keys.Where(k => k == null).ToList()) ignoreUntil.Remove(dead);

        foreach (var s in SamplesInZone())
        {
            var g = s.GetComponent<XRGrabInteractable>();
            if (g != null && g.isSelected) continue;     // se evalúa cuando el jugador la suelta
            if (ignoreUntil.TryGetValue(s, out float until) && Time.time < until) continue;
            Evaluate(s);
        }

        PushOutForeignObjects();
    }

    // La evaluadora solo recibe SampleItem (botella o vaso). Cualquier otra cosa suelta en la ranura
    // (átomos, barras, moléculas, tapas, canastas...) se devuelve hacia afuera.
    readonly Dictionary<Rigidbody, float> foreignUntil = new Dictionary<Rigidbody, float>();

    void PushOutForeignObjects()
    {
        foreach (var dead in foreignUntil.Keys.Where(k => k == null).ToList()) foreignUntil.Remove(dead);
        var seen = new HashSet<Rigidbody>();
        foreach (var c in OverlapZone())
        {
            var rb = c.attachedRigidbody;
            if (rb == null || rb.isKinematic || !seen.Add(rb)) continue;
            if (transform.IsChildOf(rb.transform) || rb.transform.IsChildOf(transform.root)) continue;   // la propia máquina
            if (rb.GetComponentInParent<SampleItem>() != null) continue;
            if (rb.GetComponent<BottleCap>() != null && IsCapOnBottle(rb)) continue;    // tapa puesta en una botella
            var g = rb.GetComponent<XRGrabInteractable>();
            if (g != null && g.isSelected) continue;
            if (foreignUntil.TryGetValue(rb, out float until) && Time.time < until) continue;
            foreignUntil[rb] = Time.time + rejectCooldown;
            rb.linearVelocity = transform.TransformDirection(rejectDirection).normalized * rejectSpeed + Vector3.up;
            onRejected.Invoke();
            Debug.Log($"[DeliveryEvaluator] {rb.name} no es una muestra (botella o vaso): se devuelve.", rb);
        }
    }

    static bool IsCapOnBottle(Rigidbody cap)
    {
        var bc = cap.GetComponent<BottleCap>();
        return bc != null && bc.IsOn;
    }

    void Evaluate(SampleItem s)
    {
        if (!s.IsFilled) { Reject(s, RejectReason.Empty); return; }
        if (requireClassification && !s.classified) { Reject(s, RejectReason.NotClassified); return; }

        var cur = orders != null ? orders.Current : null;
        if (cur == null) { Reject(s, RejectReason.NoActiveOrder); return; }
        if (cur.state == OrderState.Lost || cur.timeRemaining <= 0f) { Reject(s, RejectReason.Expired); return; }   // se acabó el tiempo: no recibe la muestra
        if (cur.state != OrderState.Active) { Reject(s, RejectReason.NoActiveOrder); return; }
        if (cur.recipe != s.recipe)                                                           // no es lo pedido: 0 puntos y el pedido se pierde
        {
            Debug.Log($"[DeliveryEvaluator] Entregaron {s.recipe.displayName} pero el pedido era {cur.recipe.displayName}: el pedido se pierde (0 puntos).", this);
            Reject(s, RejectReason.WrongRecipe);
            orders.Fail(cur);
            return;
        }
        var order = orders.FindActive(s.recipe);
        if (order == null) { Reject(s, RejectReason.NoMatchingOrder); return; }

        int basePts = s.recipe.basePoints;
        float fraction = order.timeLimit > 0f ? Mathf.Clamp01(order.timeRemaining / order.timeLimit) : 0f;
        int bonus = Mathf.RoundToInt(basePts * maxBonus * fraction);
        // Tapa puesta: el socket de la tapa es un FilteredSocket hijo de la botella y tiene la tapa encajada
        var capSocket = s.GetComponentInChildren<FilteredSocket>();
        int sealedPts = capSocket != null && capSocket.hasSelection ? sealedBonusPoints : 0;

        var result = new DeliveryResult
        {
            recipe = s.recipe,
            basePoints = basePts,
            bonusPoints = bonus,
            sealedBonus = sealedPts,
            totalPoints = basePts + bonus + sealedPts,
            timeUsed = order.TimeUsed
        };

        orders.Complete(order);
        ShowBreakdown(result);

        if (recycleContainers)
        {
            s.ResetContents();                    // el contenedor se vacía y vuelve al jugador
            ignoreUntil[s] = Time.time + rejectCooldown;
            Push(s);
        }
        else
        {
            Destroy(s.gameObject);                // la muestra se consume
        }
        onDelivered.Invoke();
        Delivered?.Invoke(result);
    }

    // ---------------------------------------------------------------
    // Desglose con conteo animado (solo visual: los puntos ya se enviaron al ScoreManager)
    // ---------------------------------------------------------------
    void ShowBreakdown(DeliveryResult r)
    {
        if (breakdownText == null) return;
        if (countRoutine != null) StopCoroutine(countRoutine);
        countRoutine = StartCoroutine(CountBreakdown(r));
    }

    IEnumerator CountBreakdown(DeliveryResult r)
    {
        var lines = new List<string>
        {
            r.recipe.displayName,
            $"Base: {r.basePoints}",
            $"Tiempo: {Mathf.RoundToInt(r.timeUsed)} s",
            $"Bonus: +{r.bonusPoints}"
        };
        if (r.sealedBonus > 0) lines.Add($"Tapa: +{r.sealedBonus}");

        // Las líneas aparecen una por una
        string shown = "";
        foreach (var line in lines)
        {
            shown += line + "\n";
            breakdownText.text = shown;
            onCountTick.Invoke();
            yield return new WaitForSeconds(lineDelay);
        }

        // El total cuenta de 0 a su valor
        float t = 0f, nextTick = 0f;
        int last = -1;
        while (t < countDuration)
        {
            t += Time.deltaTime;
            int v = Mathf.RoundToInt(Mathf.Lerp(0f, r.totalPoints, Mathf.Clamp01(t / countDuration)));
            if (v != last)
            {
                last = v;
                breakdownText.text = shown + $"Total: {v}";
                if (Time.time >= nextTick) { onCountTick.Invoke(); nextTick = Time.time + 0.05f; }   // máx. 20 tics por segundo
            }
            yield return null;
        }

        breakdownText.text = shown + $"Total: {r.totalPoints}";
        onCountFinished.Invoke();
        countRoutine = null;
    }

    void Reject(SampleItem s, RejectReason reason)
    {
        ignoreUntil[s] = Time.time + rejectCooldown;
        Push(s);
        onRejected.Invoke();
        Rejected?.Invoke(s, reason);
    }

    // Empuja el contenedor fuera de la ranura, en la dirección configurada
    void Push(SampleItem s)
    {
        var rb = s.GetComponent<Rigidbody>();
        if (rb == null) return;
        rb.isKinematic = false;
        rb.linearVelocity = transform.TransformDirection(rejectDirection).normalized * rejectSpeed + Vector3.up;
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