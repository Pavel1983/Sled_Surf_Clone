using UnityEngine;

/// <summary>
/// What the sled leaves behind on the snow: a cleared track in the road's snow mask and a spray of flakes
/// at the contact point. The sled calls it on every physics step it is sliding on the road.
/// </summary>
[DisallowMultipleComponent]
public class SnowTrail : MonoBehaviour
{
    [SerializeField] private ParticleSystem snowSpray;
    [Tooltip("Flake size multiplier while the sled is barely moving.")]
    [SerializeField] private float spraySizeSlow = 0.5f;
    [Tooltip("Flake size multiplier at Spray Full Speed and above.")]
    [SerializeField] private float spraySizeFast = 2.5f;
    [Tooltip("Speed, in meters per second, where the flakes reach their largest size.")]
    [SerializeField] private float sprayFullSpeed = 28f;

    private bool trailValid;
    private Vector3 lastTrailPoint;
    private float sprayBudget;

    /// <summary>
    /// Clears the snow under the sled for this step and throws up spray.
    /// </summary>
    /// <param name="t">Position along the road, from 0 to 1.</param>
    /// <param name="u">Position across the road, from 0 to 1.</param>
    /// <param name="tangent">Road direction at that point.</param>
    /// <param name="radius">Half the width of the cleared track, in meters.</param>
    public void Leave(SplineRoad road, Rigidbody body, float t, float u, Vector3 tangent, Vector3 contactPoint, Vector3 contactNormal, float radius)
    {
        // Sample coverage before the clear. The disc sits behind the sled so the next
        // friction sample under it still sees the snow it is sliding on.
        float coverage = road.SnowCoverage(t, u);
        float clearRadius = Mathf.Max(radius, 0.05f);
        Vector3 behind = body.position - tangent * (clearRadius * 1.7f);
        if (!trailValid)
        {
            lastTrailPoint = behind;
        }

        road.ClearSnowAlong(lastTrailPoint, behind, clearRadius, t);
        lastTrailPoint = behind;
        trailValid = true;
        EmitSpray(body, radius, contactPoint, contactNormal, tangent, coverage);
    }

    /// <summary>
    /// Ends the current stretch of track. After a jump the next stretch starts where the sled lands,
    /// and the gap in between keeps its snow.
    /// </summary>
    public void Break()
    {
        trailValid = false;
    }

    /// <summary>Forgets the track and removes the flakes still in the air.</summary>
    public void Clear()
    {
        trailValid = false;
        sprayBudget = 0f;
        snowSpray.Clear(true);
    }

    private void EmitSpray(Rigidbody body, float radius, Vector3 point, Vector3 normal, Vector3 tangent, float coverage)
    {
        if (coverage < 0.2f)
        {
            return;
        }

        float speed = body.linearVelocity.magnitude;
        if (speed < 1.5f)
        {
            return;
        }

        if (!snowSpray.isPlaying)
        {
            snowSpray.Play();
        }

        sprayBudget += speed * Mathf.Clamp01(coverage) * Time.fixedDeltaTime * 8f;
        int count = Mathf.Min(Mathf.FloorToInt(sprayBudget), 6);
        if (count <= 0)
        {
            return;
        }

        sprayBudget -= count;
        if (normal.sqrMagnitude < 0.0001f)
        {
            normal = Vector3.up;
        }
        else
        {
            normal.Normalize();
        }

        if (tangent.sqrMagnitude > 0.0001f)
        {
            tangent.Normalize();
        }

        Vector3 side = Vector3.Cross(normal, tangent);
        if (side.sqrMagnitude < 0.0001f)
        {
            side = Vector3.Cross(normal, Vector3.forward);
        }

        side.Normalize();

        float kick = Mathf.Lerp(2.2f, 6f, Mathf.Clamp01(speed / 28f));
        // Faster sled, bigger flakes. Each flake keeps the size it was thrown with,
        // so the plume shrinks as the sled slows down.
        float size = Mathf.Lerp(spraySizeSlow, spraySizeFast, Mathf.Clamp01(speed / Mathf.Max(1f, sprayFullSpeed)));
        for (int i = 0; i < count; i++)
        {
            float sideOffset = Random.Range(-radius * 0.85f, radius * 0.85f);
            var emit = new ParticleSystem.EmitParams
            {
                position = point + normal * 0.35f - tangent * (radius * 0.35f) + side * sideOffset,
                velocity = tangent * (speed * 0.22f)
                    + normal * Random.Range(kick * 0.45f, kick)
                    + side * Random.Range(-kick * 0.7f, kick * 0.7f),
                startSize = Random.Range(0.08f, 0.3f) * size,
                startLifetime = Random.Range(0.28f, 0.65f),
                startColor = new Color(0.96f, 0.98f, 1f, Random.Range(0.7f, 0.95f))
            };
            snowSpray.Emit(emit, 1);
        }
    }
}
