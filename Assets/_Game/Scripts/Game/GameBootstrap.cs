using UnityEngine;

/// <summary>
/// Composition root of the scene. It loads the saved progress, hands the economy to the objects
/// that read it, and connects the game events to their listeners. This happens once, in Awake.
/// </summary>
[DefaultExecutionOrder(-100)]
public class GameBootstrap : MonoBehaviour
{
    private const float MinBestGateMeters = 30f;

    [SerializeField] private Sled sled;
    [SerializeField] private SledRider rider;
    [SerializeField] private PathCoins pathCoins;
    [SerializeField] private RideHud hud;
    [SerializeField] private RunAudio runAudio;
    [Tooltip("Parents of everything the sled can hit. Obstacles under them come back when a run restarts.")]
    [SerializeField] private Transform[] obstacleRoots;
    [Tooltip("The wall that marks the player's best distance.")]
    [SerializeField] private DistanceGate bestGate;
    [Tooltip("The wall that marks the end of the run.")]
    [SerializeField] private DistanceGate finishGate;

    private ProgressRepository repository;
    private RunEconomy economy;

    /// <summary>The live economy of the running game. The editor cheats reach it through here.</summary>
    public RunEconomy Economy => economy;

    private void Awake()
    {
        repository = new ProgressRepository();
        economy = new RunEconomy(repository.Load());
        economy.Changed += SaveProgress;

        sled.Initialize(economy);
        hud.Initialize(economy);

        sled.RunFinished += BankRun;
        sled.Armed += rider.SitDown;
        sled.Crashed += rider.PlayCrash;
        sled.RunReset += rider.StandUp;

        sled.Armed += runAudio.PlaySitDown;
        sled.Slowed += runAudio.PlaySlowHit;
        sled.Crashed += runAudio.PlayCrash;
        pathCoins.Picked += runAudio.PlayCoin;
        hud.Clicked += runAudio.PlayClick;

        for (int i = 0; i < obstacleRoots.Length; i++)
        {
            Obstacle[] obstacles = obstacleRoots[i].GetComponentsInChildren<Obstacle>(true);
            for (int j = 0; j < obstacles.Length; j++)
            {
                sled.RunReset += obstacles[j].Restore;
            }
        }
    }

    // In Start, not Awake: the road has to be enabled before anything can be stood on it.
    private void Start()
    {
        sled.RunReset += PlaceGates;
        PlaceGates();
    }

    private void PlaceGates()
    {
        finishGate.Place(sled.FinishMeters);

        // A record right behind the start pad or on top of the finish would only be noise.
        float best = economy.BestDistanceMeters;
        if (best > MinBestGateMeters && best < sled.FinishMeters - MinBestGateMeters)
        {
            bestGate.Place(best);
        }
        else
        {
            bestGate.Remove();
        }
    }

    private void BankRun()
    {
        economy.CommitRun(pathCoins.Collected, sled.RunDistanceMeters);
    }

    private void SaveProgress()
    {
        repository.Save(economy.Progress);
    }
}
