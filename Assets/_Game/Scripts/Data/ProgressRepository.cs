using UnityEngine;

/// <summary>
/// Reads and writes <see cref="PlayerProgress"/> in PlayerPrefs. It holds no game rules.
/// </summary>
public class ProgressRepository
{
    private const string CoinsKey = "SledSurf.Coins";
    private const string LaunchKey = "SledSurf.Power";
    private const string FrictionKey = "SledSurf.Speed";
    private const string IncomeKey = "SledSurf.Income";
    private const string RunsKey = "SledSurf.Runs";

    public PlayerProgress Load()
    {
        return new PlayerProgress
        {
            Coins = Mathf.Max(0, PlayerPrefs.GetInt(CoinsKey, 0)),
            LaunchLevel = Mathf.Max(0, PlayerPrefs.GetInt(LaunchKey, 0)),
            FrictionLevel = Mathf.Max(0, PlayerPrefs.GetInt(FrictionKey, 0)),
            IncomeLevel = Mathf.Max(0, PlayerPrefs.GetInt(IncomeKey, 0)),
            RunsCompleted = Mathf.Max(0, PlayerPrefs.GetInt(RunsKey, 0))
        };
    }

    public void Save(PlayerProgress progress)
    {
        PlayerPrefs.SetInt(CoinsKey, progress.Coins);
        PlayerPrefs.SetInt(LaunchKey, progress.LaunchLevel);
        PlayerPrefs.SetInt(FrictionKey, progress.FrictionLevel);
        PlayerPrefs.SetInt(IncomeKey, progress.IncomeLevel);
        PlayerPrefs.SetInt(RunsKey, progress.RunsCompleted);
        PlayerPrefs.Save();
    }
}
