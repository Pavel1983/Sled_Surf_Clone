using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor shortcuts for testing the shop: wipe the saved progress, or add coins.
/// Both work in Edit Mode and in Play Mode. In Play Mode they go through the running economy,
/// which would otherwise write its own copy of the progress back over the change.
/// </summary>
public static class GameCheats
{
    private const int SmallGift = 1000;
    private const int LargeGift = 100000;

    [MenuItem("Tools/Game/Clear Saved Progress")]
    private static void ClearProgress()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Clear saved progress",
            "This deletes every PlayerPrefs value of the project: coins, upgrade levels and the run count.",
            "Clear",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        if (TryGetRunningEconomy(out RunEconomy economy))
        {
            economy.ClearProgress();
        }

        Debug.Log("Game cheats: saved progress cleared.");
    }

    [MenuItem("Tools/Game/Add 1 000 Coins")]
    private static void AddSmallGift()
    {
        AddCoins(SmallGift);
    }

    [MenuItem("Tools/Game/Add 100 000 Coins")]
    private static void AddLargeGift()
    {
        AddCoins(LargeGift);
    }

    private static void AddCoins(int amount)
    {
        if (TryGetRunningEconomy(out RunEconomy economy))
        {
            economy.AddCoins(amount);
            Debug.Log($"Game cheats: +{amount} coins, {economy.Coins} now.");
            return;
        }

        // Nothing is running, so the save is the only copy of the progress.
        var repository = new ProgressRepository();
        PlayerProgress progress = repository.Load();
        progress.Coins += amount;
        repository.Save(progress);
        Debug.Log($"Game cheats: +{amount} coins, {progress.Coins} now.");
    }

    private static bool TryGetRunningEconomy(out RunEconomy economy)
    {
        economy = null;
        if (!Application.isPlaying)
        {
            return false;
        }

        GameBootstrap bootstrap = Object.FindAnyObjectByType<GameBootstrap>();
        if (bootstrap == null)
        {
            return false;
        }

        economy = bootstrap.Economy;
        return economy != null;
    }
}
