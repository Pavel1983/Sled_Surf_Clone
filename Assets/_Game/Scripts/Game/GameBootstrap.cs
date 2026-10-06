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
    [Tooltip("Parent of every obstacle of the level. Obstacles under it come back when a run restarts.")]
    [SerializeField] private Transform obstaclesRoot;

    private ProgressRepository repository;
    private RunEconomy economy;

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

        Obstacle[] obstacles = obstaclesRoot.GetComponentsInChildren<Obstacle>(true);
        for (int i = 0; i < obstacles.Length; i++)
        {
            launch.RunReset += obstacles[i].Restore;
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
