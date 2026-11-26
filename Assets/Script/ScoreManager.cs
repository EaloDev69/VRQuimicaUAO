using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// Puntaje y métricas verificables (GDD 8).
/// Escucha a la Evaluadora (entregas), a la Transmutadora (fallos y derrames) y al OrderManager.
public class ScoreManager : MonoBehaviour
{
    [Header("Fuentes")]
    public OrderManager orders;
    public DeliveryEvaluator evaluator;
    public TransmutatorMachine machine;

    [Header("Penalizaciones (propuesta, se ajusta en pruebas)")]
    public int failPenalty = 20;             // GDD 8.1: fallo -20
    public int spillPenalty = 20;            // el GDD no lo define: pendiente de confirmar

    [Header("Estrellas (propuesta)")]
    public int twoStarsScore = 700;
    public int threeStarsScore = 1100;

    [Header("Pantalla (opcional)")]
    public TMP_Text scoreText;

    // Estado y métricas
    public int Score { get; private set; }
    public int Deliveries { get; private set; }
    public int Failures { get; private set; }
    public int Spills { get; private set; }
    public float TotalDeliveryTime { get; private set; }
    public IReadOnlyCollection<string> CompletedRecipes => completed;

    public float AverageTimePerOrder => Deliveries == 0 ? 0f : TotalDeliveryTime / Deliveries;
    public float FirstTryPrecision => Deliveries == 0 ? 0f : (float)firstTry / Deliveries;   // % correctas al primer intento

    public int Stars => Score >= threeStarsScore ? 3 : Score >= twoStarsScore ? 2 : 1;
    public string StarTitle => Stars == 3 ? "Maestro Molecular" : Stars == 2 ? "Técnico" : "Aprendiz";

    public event Action<int> ScoreChanged;

    readonly HashSet<string> completed = new HashSet<string>();
    int firstTry;
    int failsThisOrder;

    void OnEnable()
    {
        if (evaluator != null) evaluator.Delivered += OnDelivered;
<<<<<<< HEAD
        if (machine != null) { machine.Failed += OnFailed; machine.Spilled += OnSpilled; machine.LeverFailed += OnLeverFailed; }
=======
        if (machine != null) { machine.Failed += OnFailed; machine.Spilled += OnSpilled; }
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
        if (orders != null) orders.OrderPublished += OnOrderPublished;
        Refresh();
    }

    void OnDisable()
    {
        if (evaluator != null) evaluator.Delivered -= OnDelivered;
<<<<<<< HEAD
        if (machine != null) { machine.Failed -= OnFailed; machine.Spilled -= OnSpilled; machine.LeverFailed -= OnLeverFailed; }
=======
        if (machine != null) { machine.Failed -= OnFailed; machine.Spilled -= OnSpilled; }
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
        if (orders != null) orders.OrderPublished -= OnOrderPublished;
    }

    void OnOrderPublished(Order _) { failsThisOrder = 0; }

    void OnDelivered(DeliveryResult r)
    {
        Score += r.totalPoints;
        Deliveries++;
        TotalDeliveryTime += r.timeUsed;
        completed.Add(r.recipe.formula);
        if (failsThisOrder == 0) firstTry++;
        failsThisOrder = 0;
        Refresh();
    }

    void OnFailed(ValidationState _)
    {
        Failures++;
        failsThisOrder++;
        Score = Mathf.Max(0, Score - failPenalty);
        Refresh();
    }

<<<<<<< HEAD
    void OnLeverFailed(LeverFail _)
    {
        Failures++;
        failsThisOrder++;
        Score = Mathf.Max(0, Score - failPenalty);
        Refresh();
    }

=======
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
    void OnSpilled()
    {
        Spills++;
        Score = Mathf.Max(0, Score - spillPenalty);
        Refresh();
    }

    public void ResetRun()
    {
        Score = Deliveries = Failures = Spills = firstTry = failsThisOrder = 0;
        TotalDeliveryTime = 0f;
        completed.Clear();
        Refresh();
    }

    void Refresh()
    {
        if (scoreText != null) scoreText.text = $"Puntos: {Score}";
        ScoreChanged?.Invoke(Score);
    }
}
