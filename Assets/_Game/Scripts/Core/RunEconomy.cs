using System;
using UnityEngine;

/// <summary>
/// The rules of coins and upgrades: what a run pays, what an upgrade costs and what it changes.
/// It owns the progress while the game runs. Saving is left to whoever listens to <see cref="Changed"/>.
/// </summary>
public class RunEconomy
{
    public static readonly UpgradeId[] Order =
    {
        UpgradeId.Launch,
        UpgradeId.Friction,
        UpgradeId.Income
    };

    private const int BasePerKilometer = 10;
    private const int PerKilometerStep = 5;
    private const int PipsPerRow = 5;

    /// <summary>How many looks an upgrade goes through. The shop has one picture for each.</summary>
    public const int TierCount = 5;

    /// <summary>
    /// Raised after coins or levels change, so the progress can be saved.
    /// </summary>
    public event Action Changed;

    private readonly PlayerProgress progress;
    private bool runOpen;

    public PlayerProgress Progress => progress;

    public int Coins => progress.Coins;

    public int LastRunValue { get; private set; }

    /// <summary>The farthest any finished run has gone, in meters. It changes when a run is banked, not during it.</summary>
    public float BestDistanceMeters => progress.BestDistanceMeters;

    public int IncomePerKilometer => BasePerKilometer + progress.IncomeLevel * PerKilometerStep;

    /// <summary>Multiplier on the launch speed.</summary>
    public float LaunchScale => 1f + 0.12f * progress.LaunchLevel;

    /// <summary>Multiplier on the road friction. Below 1 the sled keeps its speed longer.</summary>
    public float FrictionScale => 1f / (1f + 0.08f * progress.FrictionLevel);

    public RunEconomy(PlayerProgress progress)
    {
        this.progress = progress;
    }

    public void BeginRun()
    {
        runOpen = true;
    }

    /// <summary>Adds coins outside a run. Used by the editor cheat.</summary>
    public void AddCoins(int amount)
    {
        progress.Coins = Mathf.Max(0, progress.Coins + amount);
        Changed?.Invoke();
    }

    /// <summary>Takes the player back to no coins and no upgrades.</summary>
    public void ClearProgress()
    {
        progress.Coins = 0;
        progress.LaunchLevel = 0;
        progress.FrictionLevel = 0;
        progress.IncomeLevel = 0;
        progress.RunsCompleted = 0;
        progress.BestDistanceMeters = 0f;
        LastRunValue = 0;
        Changed?.Invoke();
    }

    /// <summary>
    /// Banks distance pay plus picked-up coins. A run is banked once: a second call for the same run adds nothing.
    /// </summary>
    public void CommitRun(int collected, float distanceMeters)
    {
        if (!runOpen)
        {
            return;
        }

        runOpen = false;
        LastRunValue = Earnings(distanceMeters, collected);
        progress.Coins += LastRunValue;
        progress.RunsCompleted++;
        progress.BestDistanceMeters = Mathf.Max(progress.BestDistanceMeters, distanceMeters);
        Changed?.Invoke();
    }

    public int Earnings(float distanceMeters, int collected)
    {
        int fromDistance = Mathf.Max(0, Mathf.RoundToInt(distanceMeters / 1000f * IncomePerKilometer));
        return fromDistance + Mathf.Max(0, collected);
    }

    /// <summary>
    /// Coins to show on screen: the saved total, plus what the open run has earned so far.
    /// </summary>
    public int DisplayCoins(int collected, float distanceMeters)
    {
        if (!runOpen)
        {
            return progress.Coins;
        }

        return progress.Coins + Earnings(distanceMeters, collected);
    }

    public int Level(UpgradeId id)
    {
        switch (id)
        {
            case UpgradeId.Launch:
                return progress.LaunchLevel;
            case UpgradeId.Friction:
                return progress.FrictionLevel;
            default:
                return progress.IncomeLevel;
        }
    }

    /// <summary>
    /// How many of the five pips are lit. Every fifth level fills the row, then it starts over.
    /// </summary>
    public int PipCount(UpgradeId id)
    {
        int level = Level(id);
        int filled = level % PipsPerRow;
        if (filled == 0 && level > 0)
        {
            return PipsPerRow;
        }

        return filled;
    }

    /// <summary>
    /// Which row of pips the upgrade is on, counted from 0. The shop shows a better-looking item for each row.
    /// A full row still belongs to its own tier: the picture changes with the first pip of the next row.
    /// </summary>
    public int Tier(UpgradeId id)
    {
        int level = Level(id);
        if (level <= 0)
        {
            return 0;
        }

        return (level - 1) / PipsPerRow;
    }

    public int NextCost(UpgradeId id)
    {
        return 6 + 5 * Level(id);
    }

    /// <summary>
    /// True when the upgrade is on its last picture and that picture's row of pips is full.
    /// A save from before the cap may hold a higher level. It can still fill its current row, then it stops.
    /// </summary>
    public bool IsMaxed(UpgradeId id)
    {
        return Tier(id) >= TierCount - 1 && PipCount(id) == PipsPerRow;
    }

    public bool CanBuy(UpgradeId id)
    {
        return !runOpen && !IsMaxed(id) && progress.Coins >= NextCost(id);
    }

    public bool TryBuy(UpgradeId id)
    {
        if (!CanBuy(id))
        {
            return false;
        }

        progress.Coins -= NextCost(id);
        switch (id)
        {
            case UpgradeId.Launch:
                progress.LaunchLevel++;
                break;
            case UpgradeId.Friction:
                progress.FrictionLevel++;
                break;
            default:
                progress.IncomeLevel++;
                break;
        }

        Changed?.Invoke();
        return true;
    }
}
