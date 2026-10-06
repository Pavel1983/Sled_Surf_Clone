using UnityEngine;

/// <summary>
/// How the sled answers the stick. It turns the stick into a heading change and keeps the heading
/// inside a cone around the road. It only rotates the velocity it is given: the sled decides when to call it
/// and tells it what the ground under it looks like.
/// </summary>
[DisallowMultipleComponent]
public class SledSteering : MonoBehaviour
{
    // Below this speed, in meters per second, there is nothing to carve.
    // A sled does not spin while it is nearly stopped.
    private const float MinSpeed = 2f;

    [Tooltip("Sideways acceleration at full stick on the road, in g. The turn rate is this divided by the speed, so the sled turns sharply when slow and gently when fast.")]
    [SerializeField] private float groundLateralG = 1.2f;
    [Tooltip("Sideways acceleration at full stick in the air, in g. Lower than on the road, so a jump corrects less.")]
    [SerializeField] private float airLateralG = 0.4f;
    [Tooltip("Fastest the heading may turn, in degrees per second. It caps the turn at low speed, where the acceleration limit alone would spin the sled.")]
    [SerializeField] private float maxTurnRate = 120f;
    [Tooltip("Seconds the turn takes to catch up with the stick. Larger feels heavier: the turn builds up and fades out instead of snapping. 0 follows the stick at once.")]
    [SerializeField] private float steerSmoothTime = 0.25f;
    [Tooltip("Stick response curve. 1 is linear. Larger makes a small stick move gentler, while full stick still gives the full turn.")]
    [SerializeField] private float steerCurve = 1.6f;
    [Tooltip("Full width, in degrees, of the cone the sled may travel in, centered on the road direction. 60 keeps the heading within 30 degrees to either side. 0 turns the limit off.")]
    [Range(0f, 180f)]
    [SerializeField] private float headingCone = 60f;

    private float steer;
    private float steerVelocity;

    /// <summary>
    /// Horizontal stick deflection, from -1 (left) to 1 (right).
    /// </summary>
    public float Input { get; set; }

    /// <summary>True while the eased stick value is large enough to turn the sled.</summary>
    public bool IsTurning => Mathf.Abs(steer) >= 0.002f;

    public bool LimitsHeading => headingCone > 0f;

    /// <summary>
    /// Eases the turn toward the stick, so a flick does not jerk the sled
    /// and the carve fades out after the finger lifts. Call once per physics step.
    /// </summary>
    public void Smooth(float deltaTime)
    {
        float target = Mathf.Clamp(Input, -1f, 1f);
        target = Mathf.Sign(target) * Mathf.Pow(Mathf.Abs(target), Mathf.Max(1f, steerCurve));
        steer = steerSmoothTime > 0.001f
            ? Mathf.SmoothDamp(steer, target, ref steerVelocity, steerSmoothTime, Mathf.Infinity, deltaTime)
            : target;
    }

    /// <summary>
    /// Rotates the velocity around <paramref name="axis"/> by the turn the stick asks for.
    /// Returns false when the sled is too slow to turn and the velocity was left alone.
    /// </summary>
    /// <param name="grounded">True on the road, false in the air. The air allows less sideways acceleration.</param>
    /// <param name="axis">The ground normal on the road, world up in the air.</param>
    public bool Turn(Rigidbody body, bool grounded, Vector3 axis, float deltaTime)
    {
        Vector3 velocity = body.linearVelocity;
        Vector3 vertical = Vector3.Project(velocity, axis);
        Vector3 planar = velocity - vertical;
        float speed = planar.magnitude;
        if (speed < MinSpeed)
        {
            return false;
        }

        // The stick asks for a share of the sideways acceleration the sled can take. On a curve
        // that acceleration is speed times turn rate, so the same stick turns less the faster it goes.
        float lateralAcceleration = steer * (grounded ? groundLateralG : airLateralG) * Physics.gravity.magnitude;
        float turnRate = lateralAcceleration / speed * Mathf.Rad2Deg;
        turnRate = Mathf.Clamp(turnRate, -maxTurnRate, maxTurnRate);
        body.linearVelocity = vertical + Quaternion.AngleAxis(turnRate * deltaTime, axis) * planar;
        return true;
    }

    /// <summary>
    /// Keeps the direction of travel inside the heading cone around the road direction.
    /// The angle is measured in the plane of the road, so a slope does not count against it.
    /// Returns true when the velocity had to be turned back.
    /// </summary>
    public bool LimitHeading(Rigidbody body, Vector3 roadTangent, Vector3 roadUp)
    {
        Vector3 velocity = body.linearVelocity;
        Vector3 vertical = Vector3.Project(velocity, roadUp);
        Vector3 planar = velocity - vertical;
        Vector3 forward = Vector3.ProjectOnPlane(roadTangent, roadUp);
        float speed = planar.magnitude;
        // A sled that is nearly stopped or sliding back has no heading to hold.
        // Turning it around here would also hide the backward slide that ends the run.
        if (speed < MinSpeed || forward.sqrMagnitude < 0.0001f || Vector3.Dot(planar, forward) <= 0f)
        {
            return false;
        }

        float halfCone = headingCone * 0.5f;
        float angle = Vector3.SignedAngle(forward, planar, roadUp);
        if (Mathf.Abs(angle) <= halfCone)
        {
            return false;
        }

        Vector3 limited = Quaternion.AngleAxis(Mathf.Sign(angle) * halfCone, roadUp) * forward.normalized;
        body.linearVelocity = vertical + limited * speed;
        return true;
    }

    /// <summary>Lets go of the stick and forgets the turn in progress.</summary>
    public void Clear()
    {
        Input = 0f;
        steer = 0f;
        steerVelocity = 0f;
    }
}
