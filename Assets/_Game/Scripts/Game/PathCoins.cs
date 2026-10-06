using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Places spinning coins along the road. Driving through one collects it.
/// A new run puts them back and clears the count.
/// </summary>
[DisallowMultipleComponent]
public class PathCoins : MonoBehaviour
{
    private class Coin
    {
        public Transform transform;
        public float distance;
        public Vector3 basePosition;
        public Vector3 up;
        public float phase;
        public bool taken;
        public bool shown;
    }

    // Meters left (negative) or right of the road center. The road itself can be much wider.
    private static readonly float[] LaneMeters = { 0f, -3.6f, 3.6f, 0f, -6.4f, 6.4f };

    /// <summary>
    /// Raised once for every coin the sled picks up.
    /// </summary>
    public event System.Action Picked;

    [SerializeField] private SplineRoad road;
    [SerializeField] private Sled sled;
    [Tooltip("Shortest gap between coins, in meters along the road.")]
    [SerializeField] private float minSpacingMeters = 100f;
    [Tooltip("Longest gap between coins, in meters along the road.")]
    [SerializeField] private float maxSpacingMeters = 200f;
    [Tooltip("Road distance of the first coin. Keeps the launch pad clear.")]
    [SerializeField] private float firstCoinMeters = 22f;
    [Tooltip("Meters left empty before the end of the road.")]
    [SerializeField] private float endMarginMeters = 30f;
    [Tooltip("How far the coin sits above the road surface.")]
    [SerializeField] private float hoverHeight = 1.15f;
    [Tooltip("Sled distance that collects a coin, in meters.")]
    [SerializeField] private float pickupRadius = 2.05f;
    [Tooltip("Coins farther than this along the road are hidden and not animated.")]
    [SerializeField] private float visibleRangeMeters = 280f;
    [SerializeField] private int maxCoins = 240;
    [Tooltip("Material of the wide thin disc.")]
    [SerializeField] private Material rimMaterial;
    [Tooltip("Material of the smaller disc that stands proud of the rim.")]
    [SerializeField] private Material faceMaterial;

    private readonly List<Coin> coins = new List<Coin>();
    private Mesh rimMesh;
    private Mesh faceMesh;
    private bool spawned;
    private int appliedSerial = -1;

    public int Collected { get; private set; }

    private void LateUpdate()
    {
        if (!spawned)
        {
            spawned = TrySpawn();
        }

        if (sled.RunSerial != appliedSerial)
        {
            appliedSerial = sled.RunSerial;
            if (spawned)
            {
                Restore();
            }
        }

        if (!spawned)
        {
            return;
        }

        UpdateVisibility();
        Animate();
        Collect();
    }

    private void OnDestroy()
    {
        DestroyCoinMeshes();
    }

    private void DestroyCoinMeshes()
    {
        if (rimMesh != null)
        {
            Destroy(rimMesh);
        }

        if (faceMesh != null)
        {
            Destroy(faceMesh);
        }

        rimMesh = null;
        faceMesh = null;
    }

    private bool TrySpawn()
    {
        float length = road.RoadLength();
        float start = Mathf.Max(4f, firstCoinMeters);
        float playable = Mathf.Min(length, sled.FinishMeters);
        float end = playable - Mathf.Max(0f, endMarginMeters);
        if (end <= start + 1f)
        {
            return false;
        }

        float minGap = Mathf.Max(6f, Mathf.Min(minSpacingMeters, maxSpacingMeters));
        float maxGap = Mathf.Max(minGap, Mathf.Max(minSpacingMeters, maxSpacingMeters));
        int cap = Mathf.Max(1, maxCoins);

        float width = EstimateWidth();
        // A coin is two discs: a wide thin rim and a smaller, thicker face that stands proud of it.
        rimMesh = DiscMesh.Create(0.5f, 0.09f, 18);
        faceMesh = DiscMesh.Create(0.31f, 0.15f, 14);

        float distance = start;
        for (int i = 0; i < cap && distance <= end; i++)
        {
            float t = distance / length;
            float u = LaneU(width, LaneMeters[i % LaneMeters.Length]);
            if (road.TryGetSurface(t, u, out Vector3 position, out Vector3 tangent, out Vector3 up))
            {
                if (up.sqrMagnitude < 0.0001f)
                {
                    up = Vector3.up;
                }

                up.Normalize();
                Vector3 forward = Vector3.ProjectOnPlane(tangent, up);
                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = Vector3.forward;
                }

                var coinObject = new GameObject("Coin");
                coinObject.transform.SetParent(transform, false);
                coinObject.transform.SetPositionAndRotation(
                    position + up * hoverHeight,
                    Quaternion.LookRotation(forward.normalized, up));
                AddPart(coinObject.transform, "Rim", rimMesh, rimMaterial);
                AddPart(coinObject.transform, "Face", faceMesh, faceMaterial);

                coins.Add(new Coin
                {
                    transform = coinObject.transform,
                    distance = distance,
                    basePosition = position + up * hoverHeight,
                    up = up,
                    phase = (i * 1.37f) % (Mathf.PI * 2f),
                    shown = true
                });
            }

            distance += Mathf.Lerp(minGap, maxGap, Random.value);
        }

        if (coins.Count > 0)
        {
            return true;
        }

        DestroyCoinMeshes();
        return false;
    }

    private float EstimateWidth()
    {
        const float sampleU = 0.12f;
        if (!road.TryGetSurface(0.05f, 0.5f, out Vector3 center, out _, out Vector3 up))
        {
            return 14f;
        }

        if (!road.TryGetSurface(0.05f, 0.5f + sampleU, out Vector3 side, out _, out _))
        {
            return 14f;
        }

        if (up.sqrMagnitude < 0.0001f)
        {
            up = Vector3.up;
        }

        float span = Vector3.ProjectOnPlane(side - center, up).magnitude;
        return Mathf.Max(1f, span / sampleU);
    }

    private static float LaneU(float width, float metersFromCenter)
    {
        float half = Mathf.Max(0.5f, width * 0.5f);
        float offset = Mathf.Clamp(metersFromCenter, -half * 0.72f, half * 0.72f);
        return Mathf.Clamp01(0.5f + offset / Mathf.Max(0.01f, width));
    }

    private void Restore()
    {
        Collected = 0;
        for (int i = 0; i < coins.Count; i++)
        {
            Coin coin = coins[i];
            coin.taken = false;
            coin.shown = false;
            if (coin.transform != null)
            {
                coin.transform.gameObject.SetActive(false);
            }
        }
    }

    private void UpdateVisibility()
    {
        float along = sled.DistanceMeters;
        float reach = Mathf.Max(40f, visibleRangeMeters);
        for (int i = 0; i < coins.Count; i++)
        {
            Coin coin = coins[i];
            if (coin.taken || coin.transform == null)
            {
                continue;
            }

            bool show = Mathf.Abs(coin.distance - along) <= reach;
            if (coin.shown == show)
            {
                continue;
            }

            coin.shown = show;
            coin.transform.gameObject.SetActive(show);
        }
    }

    private void Animate()
    {
        float spin = 220f * Time.deltaTime;
        float bob = Time.time * 2.4f;
        for (int i = 0; i < coins.Count; i++)
        {
            Coin coin = coins[i];
            if (!coin.shown || coin.taken || coin.transform == null)
            {
                continue;
            }

            coin.transform.position = coin.basePosition + coin.up * (Mathf.Sin(bob + coin.phase) * 0.14f);
            coin.transform.Rotate(0f, spin, 0f, Space.Self);
        }
    }

    private void Collect()
    {
        Rigidbody body = sled.Body;
        if (body.isKinematic || !sled.IsRiding)
        {
            return;
        }

        Vector3 position = body.position;
        float along = sled.DistanceMeters;
        float window = Mathf.Max(10f, sled.SurfaceSpeed * Time.deltaTime * 4f + pickupRadius);
        float limit = pickupRadius * pickupRadius;
        for (int i = 0; i < coins.Count; i++)
        {
            Coin coin = coins[i];
            if (coin.taken)
            {
                continue;
            }

            if (Mathf.Abs(coin.distance - along) > window)
            {
                continue;
            }

            if ((coin.basePosition - position).sqrMagnitude > limit)
            {
                continue;
            }

            coin.taken = true;
            Collected++;
            Picked?.Invoke();
            if (coin.transform != null)
            {
                coin.transform.gameObject.SetActive(false);
            }
        }
    }

    private static void AddPart(Transform parent, string name, Mesh mesh, Material material)
    {
        var part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = Vector3.zero;
        // The disc is modeled lying flat. Stand it on its edge so it spins like a coin, face to the rider.
        part.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var filter = part.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        renderer.allowOcclusionWhenDynamic = false;
    }
}
