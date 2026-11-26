using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

public enum OrderState { Waiting, Active, Delivered, Lost }

public class Order
{
    public RecipeData recipe;
    public float timeLimit;
    public float timeRemaining;
    public OrderState state;
    public float TimeUsed => timeLimit - timeRemaining;
}

/// Pedidos y temporizador (GDD 4.7 y 7.1).
/// - El pedido se publica en espera (el tiempo NO corre).
/// - Al pulsar Aceptar (LabButton -> OrderManager.AcceptCurrent) pasa a en curso y corre el tiempo.
/// - Se entrega en la Máquina Evaluadora (que usa FindActive y Complete).
/// - Un pedido a la vez. Si no se acepta, el siguiente no llega.
public class OrderManager : MonoBehaviour
{
    [Header("Datos")]
    public RecipeCatalog catalog;

    [Header("Reglas")]
    [Tooltip("Desde este nivel, agotar el tiempo pierde el pedido. Antes solo cuenta para el bonus (GDD 7.1)")]
    public int strictTimerFromLevel = 2;
    public float nextOrderDelay = 3f;
    public bool shuffleOrders = false;
<<<<<<< HEAD
    [Tooltip("Modo manual: los pedidos los acepta el GestorEncargosVR (AcceptOrder). No se usa la cola por niveles ni LevelStarter. Aquí agotar el tiempo SIEMPRE pierde el pedido")]
    public bool manualOrders = true;
=======
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
    [Range(0f, 1f)] public float lowTimeFraction = 0.2f;

    [Header("Feedback (sonido, luces)")]
    public UnityEvent onOrderPublished;
    public UnityEvent onOrderAccepted;
    public UnityEvent onLowTime;
    public UnityEvent onOrderLost;
    public UnityEvent onOrderDelivered;
    public UnityEvent onLevelCompleted;

    public event Action<Order> OrderPublished, OrderAccepted, OrderDelivered, OrderLost;
    public event Action<int> LevelCompleted;

    public Order Current { get; private set; }
    public int Level { get; private set; }
<<<<<<< HEAD
    public bool StrictTimer => manualOrders || Level >= strictTimerFromLevel;
    public bool HasActiveOrder => Current != null && Current.state == OrderState.Active;
=======
    public bool StrictTimer => Level >= strictTimerFromLevel;
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3

    readonly Queue<Order> queue = new Queue<Order>();
    bool lowFired;

    /// Arranca un nivel con las recetas de ese nivel del catálogo
    public void StartLevel(int level)
    {
        CancelInvoke();
        Level = level;
        Current = null;
        queue.Clear();

        var recipes = catalog.ForLevel(level).ToList();
        if (shuffleOrders) recipes = recipes.OrderBy(_ => UnityEngine.Random.value).ToList();

        foreach (var r in recipes)
            queue.Enqueue(new Order { recipe = r, timeLimit = r.timeLimit, timeRemaining = r.timeLimit, state = OrderState.Waiting });

        PublishNext();
    }

    public void PublishNext()
    {
        if (queue.Count == 0)
        {
            Current = null;
            LevelCompleted?.Invoke(Level);
            onLevelCompleted.Invoke();
            return;
        }
        Current = queue.Dequeue();
        lowFired = false;
        OrderPublished?.Invoke(Current);
        onOrderPublished.Invoke();
    }

<<<<<<< HEAD
    /// Modo manual: el GestorEncargosVR acepta un encargo y el tiempo empieza a correr YA.
    /// timeLimit <= 0 usa el tiempo de la receta. Devuelve false si ya hay un pedido en curso.
    public bool AcceptOrder(RecipeData recipe, float timeLimit)
    {
        if (recipe == null || HasActiveOrder) return false;
        CancelInvoke();
        float limit = timeLimit > 0f ? timeLimit : recipe.timeLimit;
        Current = new Order { recipe = recipe, timeLimit = limit, timeRemaining = limit, state = OrderState.Active };
        lowFired = false;
        OrderPublished?.Invoke(Current);
        onOrderPublished.Invoke();
        OrderAccepted?.Invoke(Current);
        onOrderAccepted.Invoke();
        return true;
    }

    /// Botón Aceptar de la pantalla (modo por niveles): empieza a correr el tiempo. No se puede aceptar dos veces.
=======
    /// Botón Aceptar de la pantalla: empieza a correr el tiempo. No se puede aceptar dos veces.
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
    public void AcceptCurrent()
    {
        if (Current == null || Current.state != OrderState.Waiting) return;
        Current.state = OrderState.Active;
        OrderAccepted?.Invoke(Current);
        onOrderAccepted.Invoke();
    }

    void Update()
    {
        if (Current == null || Current.state != OrderState.Active) return;

        Current.timeRemaining -= Time.deltaTime;

        if (!lowFired && Current.timeRemaining <= Current.timeLimit * lowTimeFraction)
        {
            lowFired = true;
            onLowTime.Invoke();
        }

        if (Current.timeRemaining > 0f) return;
        Current.timeRemaining = 0f;

        if (!StrictTimer) return;              // nivel 1: solo se pierde el bonus, no el pedido

        Current.state = OrderState.Lost;
        OrderLost?.Invoke(Current);
        onOrderLost.Invoke();
<<<<<<< HEAD
        if (!manualOrders) Invoke(nameof(PublishNext), nextOrderDelay);
    }

    /// La evaluadora lo llama cuando entregan algo que no era: el pedido se pierde
    public void Fail(Order order)
    {
        if (order == null || order.state != OrderState.Active) return;
        order.state = OrderState.Lost;
        OrderLost?.Invoke(order);
        onOrderLost.Invoke();
        if (!manualOrders) Invoke(nameof(PublishNext), nextOrderDelay);
=======
        Invoke(nameof(PublishNext), nextOrderDelay);
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
    }

    /// Pedido en curso que corresponde a esta receta (null si no hay, o si ya venció)
    public Order FindActive(RecipeData recipe)
    {
        return Current != null && Current.state == OrderState.Active && Current.recipe == recipe ? Current : null;
    }

    /// La evaluadora lo llama al aceptar una entrega
    public void Complete(Order order)
    {
        if (order == null || order.state != OrderState.Active) return;
        order.state = OrderState.Delivered;
        OrderDelivered?.Invoke(order);
        onOrderDelivered.Invoke();
<<<<<<< HEAD
        if (!manualOrders) Invoke(nameof(PublishNext), nextOrderDelay);
=======
        Invoke(nameof(PublishNext), nextOrderDelay);
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
    }
}
