using UnityEngine;

/// <summary>
/// A major obstacle. Hitting it ends the run on the spot: the sled stops dead and does not bounce away.
/// </summary>
public class CrashObstacle : Obstacle
{
    protected override void Apply(Sled sled)
    {
        sled.Crash();
    }
}
