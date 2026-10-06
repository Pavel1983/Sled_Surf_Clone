using UnityEngine;

/// <summary>
/// Turns and plants the visual on the snow without rotating the rigidbody.
/// Near the ground the model leans onto the road and its origin sits on the surface.
/// In the air it stays upright and follows the body. The blend is smoothed.
/// </summary>
public class SlideFacing : MonoBehaviour
{
    [SerializeField] private Rigidbody body;
    [SerializeField] private Sled sled;
    [SerializeField] private SplineRoad road;
    [SerializeField] private CapsuleCollider capsule;
    [SerializeField] private float minSpeed = 0.75f;
    [SerializeField] private float turnSharpness = 10f;
    [Tooltip("How fast the sit-down onto the slope catches the ground.")]
    [SerializeField] private float alignSharpness = 7f;
    [Tooltip("Gap, in meters, under which the model is fully parallel to the road.")]
    [SerializeField] private float groundedGap = 0.2f;
    [Tooltip("Gap, in meters, above which the model is upright and rides with the body.")]
    [SerializeField] private float airborneGap = 1.6f;

    private Vector3 travel = Vector3.forward;
    private float hintT = -1f;
    private float align = 1f;

    private void LateUpdate()
    {
        if (body == null)
        {
            return;
        }

        Vector3 planar = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        if (planar.sqrMagnitude > minSpeed * minSpeed)
        {
            travel = planar.normalized;
        }
        else
        {
            FaceRoadBeforeLaunch();
        }

        // The transform carries the interpolated pose. body.position only moves on physics steps,
        // so a model placed from it stutters against a camera that follows the smooth pose.
        Vector3 origin = body.transform.position;
        Vector3 normal = Vector3.up;
        Vector3 groundPoint = origin;
        float alignTarget = 0f;
        if (TryGround(origin, out groundPoint, out normal))
        {
            float height = Vector3.Dot(origin - groundPoint, normal);
            float gap = height - Radius();
            float settle = Mathf.Max(0.02f, groundedGap);
            float release = Mathf.Max(settle + 0.05f, airborneGap);
            alignTarget = 1f - Mathf.SmoothStep(settle, release, Mathf.Max(0f, gap));
        }

        float blend = 1f - Mathf.Exp(-alignSharpness * Time.deltaTime);
        align = Mathf.Lerp(align, alignTarget, blend);

        Vector3 up = Vector3.Slerp(Vector3.up, normal, align).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(travel, up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.ProjectOnPlane(transform.forward, up);
        }

        if (forward.sqrMagnitude > 0.0001f)
        {
            Quaternion target = Quaternion.LookRotation(forward.normalized, up);
            float turn = 1f - Mathf.Exp(-turnSharpness * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, turn);
        }

        transform.position = Vector3.Lerp(origin, groundPoint, align);
    }

    /// <summary>
    /// On the start pad there is no velocity to read a heading from, so take the launch direction.
    /// Without this the model keeps the default heading and can stand with her back to the road.
    /// </summary>
    private void FaceRoadBeforeLaunch()
    {
        if (sled.IsRiding)
        {
            return;
        }

        Vector3 heading = Vector3.ProjectOnPlane(sled.StableFacing, Vector3.up);
        if (heading.sqrMagnitude > 0.0001f)
        {
            travel = heading.normalized;
        }
    }

    private bool TryGround(Vector3 origin, out Vector3 point, out Vector3 normal)
    {
        point = origin;
        normal = Vector3.up;
        if (!road.TryGetSurfaceCoord(origin, hintT, out float t, out float u, out _))
        {
            return false;
        }

        hintT = t;
        if (!road.TryGetSurface(t, u, out point, out _, out normal))
        {
            return false;
        }

        if (normal.sqrMagnitude < 0.0001f)
        {
            normal = Vector3.up;
        }
        else
        {
            normal.Normalize();
        }

        Vector3 offset = origin - point;
        float height = Vector3.Dot(offset, normal);
        Vector3 lateral = offset - normal * height;
        if (lateral.sqrMagnitude > 16f)
        {
            return false;
        }

        return true;
    }

    private float Radius()
    {
        return capsule.radius;
    }
}
