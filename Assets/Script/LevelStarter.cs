using UnityEngine;

public class LevelStarter : MonoBehaviour
{
    public OrderManager orders;
    public RecipeCatalog catalog;
    public int startLevel = 1;

    void Start()
    {
        // En modo manual los encargos los acepta el GestorEncargosVR: la cola por niveles no se usa
        // (si arrancara, dejaría un pedido fantasma "en espera" como pedido actual).
        if (orders == null || orders.manualOrders) { enabled = false; return; }
        orders.LevelCompleted += OnLevelCompleted;
        orders.StartLevel(startLevel);
    }

    void OnDestroy() { if (orders != null) orders.LevelCompleted -= OnLevelCompleted; }

    void OnLevelCompleted(int level)
    {
        if (level < catalog.MaxLevel) orders.StartLevel(level + 1);
    }
}
