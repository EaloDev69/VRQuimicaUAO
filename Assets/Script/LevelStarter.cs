using UnityEngine;

public class LevelStarter : MonoBehaviour
{
    public OrderManager orders;
    public RecipeCatalog catalog;
    public int startLevel = 1;

    void Start()
    {
        orders.LevelCompleted += OnLevelCompleted;
        orders.StartLevel(startLevel);
    }

    void OnDestroy() { if (orders != null) orders.LevelCompleted -= OnLevelCompleted; }

    void OnLevelCompleted(int level)
    {
        if (level < catalog.MaxLevel) orders.StartLevel(level + 1);
    }
}
