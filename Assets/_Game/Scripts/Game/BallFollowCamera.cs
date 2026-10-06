using UnityEngine;

/// <summary>
/// Chases a point behind the sliding body.
/// P and I pull the camera toward that point. D damps the camera's own speed
/// so a moving target does not kick the rig into an unstable swing.
/// </summary>
public class BallFollowCamera : MonoBehaviour
{
    [SerializeField] private SlingshotLaunch launch;
    [SerializeField] private float distance = 8f;
    [SerializeField] private float height = 3.5f;
    [SerializeField] private float lookHeight = 0.6f;
    [SerializeField] private float minSpeed = 2f;
    [SerializeField] private float facingSharpness = 3f;
    [Tooltip("Acceleration toward the follow point, per meter of error.")]
    [SerializeField] private float kp = 6f;
    [Tooltip("Acceleration from accumulated error. Clears the lag behind a moving body.")]
    [SerializeField] private float ki = 0.5f;
    [Tooltip("Damping of the camera speed. About 2 * sqrt(Kp) is a smooth stop without wobble.")]
    [SerializeField] private float kd = 4.9f;
    [SerializeField] private float lookSharpness = 5f;
    [Tooltip("Farthest the camera may trail its follow point, in meters. The PID lag grows with the sled's speed, and this caps it. 0 turns the cap off.")]
    [SerializeField] private float maxLag = 5f;
    [Tooltip("Fraction of Max Lag where the cap starts to ease in. Below it the PID moves the camera freely.")]
    [Range(0f, 0.95f)]
    [SerializeField] private float lagKnee = 0.5f;

    private Vector3 followPosition;
    private Vector3 integral;
    private Vector3 followVelocity;
    private Vector3 stableFacing;
    private Vector3 previousTargetPosition;
    private bool hasTargetPosition;
    private bool hasSnapped;

    private void LateUpdate()
    {
        float deltaTime = Mathf.Min(Time.deltaTime, 0.05f);
        if (deltaTime <= 0.0001f)
        {
            return;
        }

        Rigidbody body = launch.Body;
        Vector3 targetPosition = body.transform.position;
        Vector3 travel = body.linearVelocity;
        if (travel.sqrMagnitude >= minSpeed * minSpeed)
        {
            Vector3 aim = travel.normalized;
            if (stableFacing.sqrMagnitude < 0.001f)
            {
                stableFacing = aim;
            }
            else
            {
                float turn = 1f - Mathf.Exp(-facingSharpness * deltaTime);
                stableFacing = Vector3.Slerp(stableFacing, aim, turn);
            }
        }
        else if (stableFacing.sqrMagnitude < 0.001f && launch.StableFacing.sqrMagnitude > 0.001f)
        {
            stableFacing = launch.StableFacing.normalized;
        }

        if (stableFacing.sqrMagnitude < 0.001f)
        {
            stableFacing = Vector3.forward;
        }

        bool teleported = hasTargetPosition
            && (targetPosition - previousTargetPosition).sqrMagnitude > 40f * 40f;
        previousTargetPosition = targetPosition;
        hasTargetPosition = true;

        if (teleported && launch.StableFacing.sqrMagnitude > 0.001f)
        {
            stableFacing = launch.StableFacing.normalized;
        }

        Vector3 desired = targetPosition - stableFacing * distance + Vector3.up * height;
        if (!hasSnapped || teleported)
        {
            Snap(desired, targetPosition);
            return;
        }

        Vector3 error = desired - followPosition;
        integral += error * deltaTime;
        integral = Vector3.ClampMagnitude(integral, 25f);

        Vector3 acceleration = kp * error + ki * integral - kd * followVelocity;
        followVelocity += acceleration * deltaTime;
        followPosition += followVelocity * deltaTime;
        // The PID point may run a little past the cap, so the camera comes back without a long wait
        // once the sled slows down.
        if (maxLag > 0f)
        {
            followPosition = desired + Vector3.ClampMagnitude(followPosition - desired, maxLag * 2f);
        }

        transform.position = desired + LimitLag(followPosition - desired);

        Vector3 lookDirection = targetPosition + Vector3.up * lookHeight - transform.position;
        if (lookDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion look = Quaternion.LookRotation(lookDirection, Vector3.up);
        float blend = 1f - Mathf.Exp(-lookSharpness * deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, blend);
    }

    /// <summary>
    /// Leaves a short lag as it is and squeezes a long one, so it approaches Max Lag and never passes it.
    /// </summary>
    private Vector3 LimitLag(Vector3 lag)
    {
        if (maxLag <= 0f)
        {
            return lag;
        }

        float length = lag.magnitude;
        float free = maxLag * Mathf.Clamp(lagKnee, 0f, 0.95f);
        if (length <= free)
        {
            return lag;
        }

        float room = maxLag - free;
        float eased = free + room * (1f - Mathf.Exp(-(length - free) / room));
        return lag * (eased / length);
    }

    private void Snap(Vector3 desired, Vector3 targetPosition)
    {
        transform.position = desired;
        followPosition = desired;
        followVelocity = Vector3.zero;
        integral = Vector3.zero;
        hasSnapped = true;

        Vector3 lookDirection = targetPosition + Vector3.up * lookHeight - desired;
        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
        }
    }
}
