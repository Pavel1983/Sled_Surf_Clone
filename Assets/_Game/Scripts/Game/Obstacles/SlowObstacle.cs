using UnityEngine;

/// <summary>
/// A minor obstacle. Hitting it keeps only part of the sled's speed.
/// </summary>
public class SlowObstacle : Obstacle
{
    [Tooltip("Fraction of the speed before the hit the sled keeps. 0.35 is about a third.")]
    [SerializeField, Range(0.05f, 0.9f)] private float keepSpeed = 0.35f;

    protected override void Apply(SlingshotLaunch sled)
    {
        sled.SlowDown(keepSpeed);
    }
}
