using UnityEngine;

/// <summary>
/// Composition root of the scene. It loads the saved progress, hands the economy to the objects
/// that read it, and connects the game events to their listeners. This happens once, in Awake.
/// </summary>
[DefaultExecutionOrder(-100)]
public class GameBootstrap : MonoBehaviour
{
    [SerializeField] private SlingshotLaunch launch;
    [SerializeField] private SledRider rider;
    [SerializeField] private PathCoins pathCoins;
    [SerializeField] private RideHud hud;
    [SerializeField] private RunAudio runAudio;
    [Tooltip("Parents of everything the sled can hit. Obstacles under them come back when a run restarts.")]
    [SerializeField] private Transform[] obstacleRoots;

    private ProgressRepository repository;
    private RunEconomy economy;

    /// <summary>The live economy of the running game. The editor cheats reach it through here.</summary>
    public RunEconomy Economy => economy;

    private void Awake()
    {
        repository = new ProgressRepository();
        economy = new RunEconomy(repository.Load());
        economy.Changed += SaveProgress;

        launch.Initialize(economy);
        hud.Initialize(economy);

        launch.RunFinished += BankRun;
        launch.Crashed += rider.PlayCrash;
        launch.RunReset += rider.StopCrash;

        launch.Armed += runAudio.PlaySitDown;
        launch.Slowed += runAudio.PlaySlowHit;
        launch.Crashed += runAudio.PlayCrash;
        pathCoins.Picked += runAudio.PlayCoin;
        hud.Clicked += runAudio.PlayClick;

        for (int i = 0; i < obstacleRoots.Length; i++)
        {
            Obstacle[] obstacles = obstacleRoots[i].GetComponentsInChildren<Obstacle>(true);
            for (int j = 0; j < obstacles.Length; j++)
            {
                launch.RunReset += obstacles[j].Restore;
            }
        }
    }

    private void BankRun()
    {
        economy.CommitRun(pathCoins.Collected, launch.RunDistanceMeters);
    }

    private void SaveProgress()
    {
        repository.Save(economy.Progress);
    }
}
