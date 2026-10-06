using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Builds a road mesh from a <see cref="SplineContainer"/>.
/// Knot height shapes hills and dips along the track.
/// <see cref="profile"/> bends the cross-section: negative is a bowl, positive is a crown.
/// Snow covers the surface in spline space. A missing mask means the whole road is covered.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(SplineContainer), typeof(MeshFilter), typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
[DisallowMultipleComponent]
public class SplineRoad : MonoBehaviour
{
    public struct RoadSurfaceHit
    {
        public Vector3 point;
        public Vector3 normal;
        public float t;
        public float u;
    }

    [Serializable]
    public struct RoadProfileKey
    {
        [Range(0f, 1f)]
        [Tooltip("Position along the road. 0 is the start, 1 is the end.")]
        public float at;

        [Tooltip("Bend here. Negative is a bowl, positive is a crown.")]
        public float amount;
    }

    public const int SculptRows = 160;
    public const int SculptColumns = 32;
    private const int LegacySnowColumns = 64;
    private const float SnowColumnMeters = 0.5f;

    // Wide roads need a texel near the sled width. Play mode uploads only the changed rect.
    private const int MaxSnowColumns = 1024;
    private const int SnowPatchExtent = 128;
    private const int Columns = 32;
    private const float SnowTexelMeters = 0.5f;

    // The snow shader displaces vertices, so the drawn mesh is finer than the collider.
    private const float VisualVertexSpacing = 0.2f;
    private const int MaxVisualSegments = 1600;
    private const int MaxVisualColumns = 72;

    // A sculpted lip can rise more than a hundred meters between two samples.
    // One triangle that long is a few pixels wide and a hundred tall, and the GPU
    // shades its whole bounding box. Split those edges.
    private const int MinSnowRows = 128;
    private const int MaxSnowRows = 8192;

    // How far along the road a hinted search is willing to look. A physics step moves the sled a few meters.
    private const float NearestWindowMeters = 48f;

    [SerializeField] private float width = 14f;
    [SerializeField] private float segmentsPerMeter = 0.5f;

    [Tooltip("Cross-section bend in meters. Negative lowers the center (concave bowl). Positive raises the center (convex crown). Zero is flat. Ignored when Profile Keys has entries.")]
    [SerializeField] private float profile;

    [Tooltip("Optional bends along the road. At 0 is the start, At 1 is the end. Amount uses the same sign as Profile. When this list is empty, Profile is used for the whole road.")]
    [SerializeField] private List<RoadProfileKey> profileKeys = new List<RoadProfileKey>();

    [SerializeField] private Material roadMaterial;

    [Tooltip("Friction where snow still covers the road. The ball uses zero friction, and this material combines with Maximum, so the sampled value is the contact friction.")]
    [SerializeField] private float friction = 0.15f;

    [Tooltip("Friction where the snow has been cleared.")]
    [SerializeField] private float iceFriction = 0.02f;

    [Tooltip("Height of the side walls in meters. Walls use zero friction and only keep the body on the road.")]
    [SerializeField] private float wallHeight = 4f;

    [Header("Debug")]
    [Tooltip("Paints each visual chunk a different color so the split is visible. Turn off to restore the snow.")]
    [SerializeField] private bool debugShowChunks;

    [SerializeField] private bool seeded;
    [SerializeField, HideInInspector] private float[] sculptHeights;

    // Empty means the whole road is snow. "0" means it is fully clear. Anything else is a base64 mask.
    // A byte array cannot be stored on this prefab instance: Unity writes one override per element and locks up.
    [SerializeField, HideInInspector] private string snowPacked;
    // 0 means a mask saved before the trail was stored wider than 64 columns.
    [SerializeField, HideInInspector] private int snowColumnCount;

    private byte[] snowMask;
    private int snowColumns = LegacySnowColumns;
    private SplineContainer splineContainer;
    private Mesh roadMesh;
    private RoadVisualChunks chunks;
    private RoadWalls walls;
    private PhysicsMaterial roadPhysics;

    // Fractional coverage left after rounding. Sparse so a 4 km mask does not allocate tens of megabytes.
    private Dictionary<int, float> snowRemainder;
    private Texture2D snowTexture;
    private Texture2D snowPatch;
    private byte[] snowPatchBytes;
    private bool snowTextureDirty = true;
    private bool snowUploadFull = true;
    private bool snowHasDirtyRect;
    private int dirtyColMin;
    private int dirtyColMax;
    private int dirtyRowMin;
    private int dirtyRowMax;

    // World length of a 4 km spline is an integration. Callers used to redo it on every stamp.
    private float cachedWorldLength = -1f;
    private bool debugShowChunksCached;

    public float CurrentFriction { get; private set; }

    // Created on first use: OnValidate can ask for a rebuild before OnEnable has run.
    private RoadVisualChunks Chunks => chunks ??= new RoadVisualChunks(transform);

    private RoadWalls Walls => walls ??= new RoadWalls(transform);

    /// <summary>The collider of the road surface. The side walls are separate colliders.</summary>
    public MeshCollider SurfaceCollider { get; private set; }

    private void OnEnable()
    {
        splineContainer = GetComponent<SplineContainer>();
        SurfaceCollider = GetComponent<MeshCollider>();
        ShowRoadMaterial();
        SeedTestSpline();
        ResizeSculptGrid();
        LoadSnowMask();
        Spline.Changed += OnSplineChanged;
#if UNITY_EDITOR
        UnityEditor.Undo.undoRedoPerformed += OnSnowUndo;
#endif
        ResizeSnowMask();
        InvalidateRoadLength();
        Rebuild();
        snowUploadFull = true;
        snowTextureDirty = true;
        FlushSnowTexture();
        // Pay the spline-length integration at load, not on the first meter of the run.
        RoadLength();
    }

    private void OnDisable()
    {
        Spline.Changed -= OnSplineChanged;
#if UNITY_EDITOR
        UnityEditor.Undo.undoRedoPerformed -= OnSnowUndo;
#endif
        ReleaseSnowTexture();
        Chunks.Release();
    }

    private void LateUpdate()
    {
        if (snowTextureDirty)
        {
            FlushSnowTexture();
        }

        if (debugShowChunks != Chunks.DebugVisible)
        {
            Chunks.SetDebugVisible(debugShowChunks);
        }
    }

    /// <summary>
    /// Raycast against the road mesh and recover the hit position in spline space.
    /// </summary>
    public bool TryRaycast(Ray ray, float maxDistance, out RoadSurfaceHit surfaceHit)
    {
        surfaceHit = default;
        var collider = GetComponent<MeshCollider>();
        if (collider == null || collider.sharedMesh == null)
        {
            return false;
        }

        if (!collider.Raycast(ray, out RaycastHit hit, maxDistance))
        {
            return false;
        }

        var mesh = collider.sharedMesh;
        var parametric = mesh.uv2;
        var indices = mesh.triangles;
        int tri = hit.triangleIndex;
        if (parametric == null || parametric.Length == 0 || tri < 0 || tri * 3 + 2 >= indices.Length)
        {
            return false;
        }

        int i0 = indices[tri * 3];
        int i1 = indices[tri * 3 + 1];
        int i2 = indices[tri * 3 + 2];
        Vector3 bary = hit.barycentricCoordinate;
        float u = parametric[i0].x * bary.x + parametric[i1].x * bary.y + parametric[i2].x * bary.z;
        float t = parametric[i0].y * bary.x + parametric[i1].y * bary.y + parametric[i2].y * bary.z;

        surfaceHit = new RoadSurfaceHit
        {
            point = hit.point,
            normal = hit.normal,
            t = Mathf.Clamp01(t),
            u = Mathf.Clamp01(u),
        };
        return true;
    }

    /// <summary>
    /// World-space point on the road. u is 0 at the left edge, 0.5 at the center, 1 at the right edge.
    /// </summary>
    public bool TryGetSurface(float t, float u, out Vector3 worldPosition, out Vector3 worldTangent, out Vector3 worldUp)
    {
        worldPosition = transform.position;
        worldTangent = transform.forward;
        worldUp = transform.up;

        if (splineContainer == null || splineContainer.Spline == null || splineContainer.Spline.Count < 2)
        {
            return false;
        }

        t = Mathf.Clamp01(t);
        u = Mathf.Clamp01(u);
        SplineUtility.Evaluate(splineContainer.Spline, t, out float3 position, out float3 tangent, out float3 up);
        float3 side = math.normalizesafe(math.cross(up, tangent), new float3(1f, 0f, 0f));
        float centered = u * 2f - 1f;
        float lift = ProfileAt(t) * (1f - centered * centered) + SampleSculpt(t, u);
        float3 local = position + side * (centered * width * 0.5f) + up * lift;

        worldPosition = transform.TransformPoint(local);
        worldTangent = transform.TransformDirection((Vector3)tangent);
        worldUp = transform.TransformDirection((Vector3)up);
        if (worldTangent.sqrMagnitude > 0.0001f)
        {
            worldTangent.Normalize();
        }

        if (worldUp.sqrMagnitude > 0.0001f)
        {
            worldUp.Normalize();
        }
        else
        {
            worldUp = Vector3.up;
        }

        return true;
    }

    /// <summary>
    /// World length of the road in meters. Cached until the spline changes.
    /// </summary>
    public float RoadLength()
    {
        if (cachedWorldLength > 0f)
        {
            return cachedWorldLength;
        }

        if (splineContainer == null)
        {
            return 1f;
        }

        cachedWorldLength = Mathf.Max(splineContainer.CalculateLength(), 0.01f);
        return cachedWorldLength;
    }

    /// <summary>
    /// Arc length from the start of the road to the closest point on the spline, in meters.
    /// </summary>
    public bool TryGetProgress(Vector3 worldPosition, out float distanceMeters, out Vector3 worldTangent)
    {
        return TryGetProgress(worldPosition, -1f, out distanceMeters, out worldTangent);
    }

    /// <summary>
    /// Arc length from the start of the road. hintT is a previous normalized coordinate, or -1 for a full search.
    /// </summary>
    public bool TryGetProgress(Vector3 worldPosition, float hintT, out float distanceMeters, out Vector3 worldTangent)
    {
        distanceMeters = 0f;
        worldTangent = transform.forward;
        if (!TryGetSurfaceCoord(worldPosition, hintT, out float t, out _, out worldTangent))
        {
            return false;
        }

        // Normalized t times length matches SplineUtility.ConvertIndexUnit to distance.
        distanceMeters = t * RoadLength();
        return true;
    }

    /// <summary>
    /// Spline coordinate under a world point. u is 0 at the left edge and 1 at the right edge.
    /// </summary>
    public bool TryGetSurfaceCoord(Vector3 worldPosition, out float t, out float u, out Vector3 worldTangent)
    {
        return TryGetSurfaceCoord(worldPosition, -1f, out t, out u, out worldTangent);
    }

    /// <summary>
    /// Spline coordinate under a world point. hintT limits the search to the sled's last position.
    /// A full search of this ribbon evaluates hundreds of samples and costs more than a millisecond.
    /// </summary>
    public bool TryGetSurfaceCoord(Vector3 worldPosition, float hintT, out float t, out float u, out Vector3 worldTangent)
    {
        t = 0f;
        u = 0.5f;
        worldTangent = transform.forward;

        if (splineContainer == null || splineContainer.Spline == null || splineContainer.Spline.Count < 2)
        {
            return false;
        }

        var spline = splineContainer.Spline;
        float3 localPoint = transform.InverseTransformPoint(worldPosition);
        t = NearestNormalizedT(spline, localPoint, hintT, out float3 nearest);
        SplineUtility.Evaluate(spline, t, out _, out float3 tangent, out float3 up);
        float3 side = math.normalizesafe(math.cross(up, tangent), new float3(1f, 0f, 0f));
        float across = math.dot(localPoint - nearest, side);
        u = Mathf.Clamp01(across / Mathf.Max(width, 0.01f) + 0.5f);

        if (math.lengthsq(tangent) > 0.0001f)
        {
            worldTangent = transform.TransformDirection((Vector3)tangent);
            if (worldTangent.sqrMagnitude > 0.0001f)
            {
                worldTangent.Normalize();
            }
        }

        return true;
    }

    /// <summary>
    /// Friction at this spot. Snow uses <see cref="friction"/>, cleared road uses <see cref="iceFriction"/>.
    /// </summary>
    public float FrictionAt(float t, float u)
    {
        float icy = Mathf.Max(0f, iceFriction);
        float snowy = Mathf.Max(0f, friction);
        return Mathf.Lerp(icy, snowy, SnowCoverage(t, u));
    }

    /// <summary>
    /// Writes the contact friction onto the road material. The ball's material combines with Maximum, so this value wins.
    /// </summary>
    public void ApplyContactFriction(float contactFriction)
    {
        contactFriction = Mathf.Max(0f, contactFriction);
        var material = RoadPhysics();
        material.dynamicFriction = contactFriction;
        material.staticFriction = contactFriction;
        CurrentFriction = contactFriction;
    }

    /// <summary>
    /// Clears snow along a world-space segment. Used by the sliding ball. This stays in the play session and is not saved.
    /// </summary>
    public void ClearSnowAlong(Vector3 from, Vector3 to, float radiusMeters, float hintT = -1f)
    {
        if (radiusMeters <= 0.001f)
        {
            return;
        }

        float distance = Vector3.Distance(from, to);
        // A solver pop is not a slide. Painting that gap marks a long snow rect and
        // used to upload the whole mask, which drops the frame rate until the sled settles.
        if (distance > 8f)
        {
            from = to;
            distance = 0f;
        }

        if (!TryGetSurfaceCoord(to, hintT, out float endT, out float endU, out _))
        {
            return;
        }

        float startT = endT;
        float startU = endU;
        if (distance > 0.05f)
        {
            TryGetSurfaceCoord(from, hintT, out startT, out startU, out _);
        }

        // Step along the spline parameter. A nearest-point search per sample walks the
        // whole 4 km ribbon once the sled is moving fast, and that is what freezes the frame.
        float step = Mathf.Max(radiusMeters * 0.35f, 0.05f);
        int steps = Mathf.Clamp(Mathf.CeilToInt(distance / step), 1, 8);
        for (int i = 0; i <= steps; i++)
        {
            float blend = i / (float)steps;
            PaintSnow(Mathf.Lerp(startU, endU, blend), Mathf.Lerp(startT, endT, blend), radiusMeters, -1f);
        }
    }

    /// <summary>
    /// Adds a radial height stamp in world space. Positive delta raises the surface.
    /// </summary>
    public void SculptStroke(Vector3 worldCenter, float radius, float deltaMeters)
    {
        if (radius <= 0.001f || splineContainer == null || splineContainer.Spline == null || splineContainer.Spline.Count < 2)
        {
            return;
        }

        ResizeSculptGrid();
        var spline = splineContainer.Spline;
        float halfWidth = width * 0.5f;
        float radiusSqr = radius * radius;

        for (int row = 0; row < SculptRows; row++)
        {
            float t = row / (float)(SculptRows - 1);
            SplineUtility.Evaluate(spline, t, out float3 position, out float3 tangent, out float3 up);
            float3 side = math.normalizesafe(math.cross(up, tangent), new float3(1f, 0f, 0f));
            float bend = ProfileAt(t);

            for (int column = 0; column < SculptColumns; column++)
            {
                float u = column / (float)(SculptColumns - 1);
                float centered = u * 2f - 1f;
                int index = row * SculptColumns + column;
                float lift = bend * (1f - centered * centered) + sculptHeights[index];
                Vector3 world = transform.TransformPoint(position + side * (centered * halfWidth) + up * lift);
                float distSqr = (world - worldCenter).sqrMagnitude;
                if (distSqr > radiusSqr)
                {
                    continue;
                }

                float falloff = 1f - Mathf.Sqrt(distSqr) / radius;
                sculptHeights[index] += deltaMeters * falloff * falloff;
            }
        }

        Rebuild();
    }

    public void ClearSculpt()
    {
        ResizeSculptGrid();
        Array.Clear(sculptHeights, 0, sculptHeights.Length);
        Rebuild();
    }

    /// <summary>
    /// Snow left at this spot. 1 is covered, 0 is clear.
    /// </summary>
    public float SnowCoverage(float t, float u)
    {
        if (snowMask == null)
        {
            return snowPacked == "0" ? 0f : 1f;
        }

        int columns = Mathf.Max(snowColumns, 2);
        if (snowMask.Length < columns * 2)
        {
            return 1f;
        }

        int rows = snowMask.Length / columns;
        float x = Mathf.Clamp01(u) * (columns - 1);
        float y = Mathf.Clamp01(t) * (rows - 1);
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(x0 + 1, columns - 1);
        int y1 = Mathf.Min(y0 + 1, rows - 1);
        float blendX = x - x0;
        float blendY = y - y0;
        float s00 = snowMask[y0 * columns + x0];
        float s10 = snowMask[y0 * columns + x1];
        float s01 = snowMask[y1 * columns + x0];
        float s11 = snowMask[y1 * columns + x1];
        return Mathf.Lerp(Mathf.Lerp(s00, s10, blendX), Mathf.Lerp(s01, s11, blendX), blendY) / 255f;
    }

    /// <summary>
    /// Paints snow in a disc on the road. Positive delta adds coverage, negative delta clears it.
    /// Delta is the coverage change at the brush center for this call.
    /// </summary>
    public void PaintSnow(float centerU, float centerT, float radiusMeters, float deltaCoverage)
    {
        if (radiusMeters <= 0.001f || Mathf.Abs(deltaCoverage) < 0.00001f)
        {
            return;
        }

        if (splineContainer == null || splineContainer.Spline == null || splineContainer.Spline.Count < 2)
        {
            return;
        }

        ResizeSnowMask();
        int columns = snowColumns;
        int rows = snowMask.Length / columns;
        snowRemainder ??= new Dictionary<int, float>();

        Vector3 lossy = transform.lossyScale;
        float scale = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z));
        scale = Mathf.Max(scale, 0.0001f);
        float worldWidth = Mathf.Max(0.01f, width * scale);
        float worldLength = RoadLength();
        float radiusU = Mathf.Max(radiusMeters / worldWidth, 0.5f / columns);
        float radiusT = Mathf.Max(radiusMeters / worldLength, 0.5f / rows);
        centerU = Mathf.Clamp01(centerU);
        centerT = Mathf.Clamp01(centerT);

        int columnMin = Mathf.Clamp(Mathf.FloorToInt((centerU - radiusU) * (columns - 1)), 0, columns - 1);
        int columnMax = Mathf.Clamp(Mathf.CeilToInt((centerU + radiusU) * (columns - 1)), 0, columns - 1);
        int rowMin = Mathf.Clamp(Mathf.FloorToInt((centerT - radiusT) * (rows - 1)), 0, rows - 1);
        int rowMax = Mathf.Clamp(Mathf.CeilToInt((centerT + radiusT) * (rows - 1)), 0, rows - 1);
        // A brush thinner than one cell still has to mark the texel under its center.
        int centerCol = Mathf.Clamp(Mathf.RoundToInt(centerU * (columns - 1)), 0, columns - 1);
        int centerRow = Mathf.Clamp(Mathf.RoundToInt(centerT * (rows - 1)), 0, rows - 1);
        if (columnMin > centerCol)
        {
            columnMin = centerCol;
        }

        if (columnMax < centerCol)
        {
            columnMax = centerCol;
        }

        if (rowMin > centerRow)
        {
            rowMin = centerRow;
        }

        if (rowMax < centerRow)
        {
            rowMax = centerRow;
        }

        bool changed = false;
        for (int row = rowMin; row <= rowMax; row++)
        {
            float along = (row / (float)(rows - 1) - centerT) / radiusT;
            for (int column = columnMin; column <= columnMax; column++)
            {
                bool center = row == centerRow && column == centerCol;
                float across = (column / (float)(columns - 1) - centerU) / radiusU;
                float reach = Mathf.Sqrt(across * across + along * along);
                if (!center && reach > 1f)
                {
                    continue;
                }
                // The cell under the brush always takes the full stamp. A wide road's texel
                // center sits half a cell off the sled, and a near-zero falloff there never
                // cleared the middle of the track.
                if (center)
                {
                    reach = 0f;
                }

                float falloff = reach < 0.5f ? 1f : (1f - reach) / 0.5f;
                falloff = Mathf.Clamp01(falloff);
                falloff *= falloff;
                int index = row * columns + column;
                snowRemainder.TryGetValue(index, out float residual);
                float next = Mathf.Clamp(snowMask[index] + deltaCoverage * 255f * falloff + residual, 0f, 255f);
                int rounded = Mathf.RoundToInt(next);
                float leftover = next - rounded;
                if (Mathf.Abs(leftover) > 0.0001f)
                {
                    snowRemainder[index] = leftover;
                }
                else
                {
                    snowRemainder.Remove(index);
                }

                byte value = (byte)rounded;
                if (value == snowMask[index])
                {
                    continue;
                }

                snowMask[index] = value;
                changed = true;
                MarkSnowDirty(column, row);
            }
        }

        if (!changed)
        {
            return;
        }

        snowTextureDirty = true;
        // Vertex colors are only the coarse copy. The play-mode trail is finer than the mesh, so it lives in the texture.
        if (!Application.isPlaying)
        {
            ApplySnowColors();
        }
    }

    /// <summary>
    /// Writes the painted mask into the saved road data. Called once when a brush stroke ends.
    /// </summary>
    public void CommitSnow()
    {
        if (snowMask == null)
        {
            return;
        }

        snowPacked = System.Convert.ToBase64String(snowMask);
        snowColumnCount = snowColumns;
        PersistSnow();
    }

    /// <summary>
    /// Covers the whole road in snow and stores that on the road so it survives saving and play mode.
    /// </summary>
    public void FillSnow()
    {
        snowMask = null;
        snowRemainder = null;
        snowPacked = "";
        snowColumnCount = 0;
        ApplySnowColors();
        snowUploadFull = true;
        snowHasDirtyRect = false;
        snowTextureDirty = true;
        FlushSnowTexture();
        PersistSnow();
    }

    /// <summary>
    /// Removes snow from the whole road.
    /// </summary>
    public void ClearAllSnow()
    {
        snowMask = null;
        snowRemainder = null;
        snowPacked = "0";
        snowColumnCount = 0;
        ApplySnowColors();
        snowUploadFull = true;
        snowHasDirtyRect = false;
        snowTextureDirty = true;
        FlushSnowTexture();
        PersistSnow();
    }

    private void OnSnowUndo()
    {
        snowRemainder = null;
        LoadSnowMask();
        ApplySnowColors();
        snowUploadFull = true;
        snowHasDirtyRect = false;
        snowTextureDirty = true;
        FlushSnowTexture();
#if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
#endif
    }

    private void ShowRoadMaterial()
    {
        if (roadMaterial != null)
        {
            GetComponent<MeshRenderer>().sharedMaterial = roadMaterial;
        }
    }

    private void SeedTestSpline()
    {
        if (seeded || splineContainer.Spline.Count > 0)
        {
            seeded = true;
            return;
        }

        // Gentle S-curve dropping toward +Z, offset from the origin so it does not sit on the character.
        float3[] knots =
        {
            new float3(25f, 10f, 5f),
            new float3(42f, 7f, 40f),
            new float3(12f, 4f, 78f),
            new float3(38f, 1.5f, 115f),
            new float3(22f, 0f, 150f),
        };

        var spline = splineContainer.Spline;
        for (int i = 0; i < knots.Length; i++)
        {
            spline.Add(new BezierKnot(knots[i]), TangentMode.AutoSmooth);
        }

        seeded = true;
    }

    private void OnSplineChanged(Spline spline, int knotIndex, SplineModification modification)
    {
        if (splineContainer != null && spline == splineContainer.Spline)
        {
            InvalidateRoadLength();
            Rebuild();
        }
    }

    private float ProfileAt(float t)
    {
        if (profileKeys == null || profileKeys.Count == 0)
        {
            return profile;
        }

        int count = profileKeys.Count;
        var order = new int[count];
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => profileKeys[a].at.CompareTo(profileKeys[b].at));

        var first = profileKeys[order[0]];
        if (t <= first.at)
        {
            return first.amount;
        }

        var last = profileKeys[order[count - 1]];
        if (t >= last.at)
        {
            return last.amount;
        }

        for (int i = 0; i < count - 1; i++)
        {
            var from = profileKeys[order[i]];
            var to = profileKeys[order[i + 1]];
            if (t > to.at)
            {
                continue;
            }

            float span = Mathf.Max(0.0001f, to.at - from.at);
            float blend = (t - from.at) / span;
            return Mathf.Lerp(from.amount, to.amount, blend);
        }

        return last.amount;
    }

    private void InvalidateRoadLength()
    {
        cachedWorldLength = -1f;
    }

    private float NearestNormalizedT(Spline spline, float3 localPoint, float hintT, out float3 nearest)
    {
        if (hintT >= 0f && hintT <= 1f && TryLocalNearest(spline, localPoint, hintT, out float localT))
        {
            nearest = SplineUtility.EvaluatePosition(spline, localT);
            return localT;
        }

        SplineUtility.GetNearestPoint(spline, localPoint, out nearest, out float globalT);
        return Mathf.Clamp01(globalT);
    }

    private bool TryLocalNearest(Spline spline, float3 localPoint, float hintT, out float t)
    {
        float length = Mathf.Max(spline.GetLength(), 0.01f);
        float window = NearestWindowMeters / length;
        float bestT = Mathf.Clamp01(hintT);
        float best = float.PositiveInfinity;
        const int samples = 8;
        for (int i = 0; i <= samples; i++)
        {
            float candidate = WrapSplineT(spline, hintT + window * (i / (float)samples - 0.5f));
            float score = math.distancesq(SplineUtility.EvaluatePosition(spline, candidate), localPoint);
            if (score < best)
            {
                best = score;
                bestT = candidate;
            }
        }

        float step = window / samples;
        for (int refine = 0; refine < 4; refine++)
        {
            step *= 0.5f;
            float left = WrapSplineT(spline, bestT - step);
            float right = WrapSplineT(spline, bestT + step);
            float leftScore = math.distancesq(SplineUtility.EvaluatePosition(spline, left), localPoint);
            float rightScore = math.distancesq(SplineUtility.EvaluatePosition(spline, right), localPoint);
            if (leftScore < best)
            {
                best = leftScore;
                bestT = left;
            }

            if (rightScore < best)
            {
                best = rightScore;
                bestT = right;
            }
        }

        // The halving search stops about 0.4 m short. Slide the rest of the way along the tangent,
        // so the result follows the point smoothly. Without this it holds still and then jumps,
        // and everything planted on the road from it stutters.
        for (int polish = 0; polish < 2; polish++)
        {
            float3 at = SplineUtility.EvaluatePosition(spline, bestT);
            float3 heading = math.normalizesafe(SplineUtility.EvaluateTangent(spline, bestT));
            bestT = WrapSplineT(spline, bestT + math.dot(localPoint - at, heading) / length);
        }

        // The closest point on a smooth curve is perpendicular to the tangent. A large leftover
        // along the tangent means the sled left the window, so the caller falls back to a full search.
        float3 nearest = SplineUtility.EvaluatePosition(spline, bestT);
        float along = math.abs(math.dot(localPoint - nearest, math.normalizesafe(SplineUtility.EvaluateTangent(spline, bestT))));
        if (along > NearestWindowMeters * 0.35f)
        {
            t = 0f;
            return false;
        }

        t = bestT;
        return true;
    }

    private static float WrapSplineT(Spline spline, float t)
    {
        if (!spline.Closed)
        {
            return Mathf.Clamp01(t);
        }

        t %= 1f;
        if (t < 0f)
        {
            t += 1f;
        }

        return t;
    }

    private void ResizeSculptGrid()
    {
        int size = SculptRows * SculptColumns;
        if (sculptHeights != null && sculptHeights.Length == size)
        {
            return;
        }

        var next = new float[size];
        if (sculptHeights != null)
        {
            Array.Copy(sculptHeights, next, Mathf.Min(sculptHeights.Length, size));
        }

        sculptHeights = next;
    }

    private float SampleSculpt(float t, float u)
    {
        if (sculptHeights == null || sculptHeights.Length != SculptRows * SculptColumns)
        {
            return 0f;
        }

        float x = Mathf.Clamp01(u) * (SculptColumns - 1);
        float y = Mathf.Clamp01(t) * (SculptRows - 1);
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(x0 + 1, SculptColumns - 1);
        int y1 = Mathf.Min(y0 + 1, SculptRows - 1);
        float blendX = x - x0;
        float blendY = y - y0;
        float h00 = sculptHeights[y0 * SculptColumns + x0];
        float h10 = sculptHeights[y0 * SculptColumns + x1];
        float h01 = sculptHeights[y1 * SculptColumns + x0];
        float h11 = sculptHeights[y1 * SculptColumns + x1];
        return Mathf.Lerp(Mathf.Lerp(h00, h10, blendX), Mathf.Lerp(h01, h11, blendX), blendY);
    }

    private int TargetSnowRows()
    {
        float length = 100f;
        if (splineContainer != null && splineContainer.Spline != null && splineContainer.Spline.Count >= 2)
        {
            length = Mathf.Max(RoadLength(), 1f);
        }

        int rows = Mathf.CeilToInt(length / SnowTexelMeters);
        return Mathf.Clamp(rows, MinSnowRows, MaxSnowRows);
    }

    private int TargetSnowColumns()
    {
        float span = Mathf.Max(width, 1f);
        int columns = Mathf.CeilToInt(span / SnowColumnMeters);
        return Mathf.Clamp(columns, LegacySnowColumns, MaxSnowColumns);
    }

    private void LoadSnowMask()
    {
        snowMask = null;
        snowColumns = TargetSnowColumns();
        if (string.IsNullOrEmpty(snowPacked) || snowPacked == "0")
        {
            return;
        }

        byte[] packed;
        try
        {
            packed = System.Convert.FromBase64String(snowPacked);
        }
        catch (System.FormatException)
        {
            snowPacked = "";
            return;
        }

        int columns = snowColumnCount >= 2 ? snowColumnCount : LegacySnowColumns;
        if (packed.Length % columns != 0)
        {
            if (packed.Length % LegacySnowColumns == 0)
            {
                columns = LegacySnowColumns;
            }
            else
            {
                snowPacked = "";
                return;
            }
        }

        snowMask = packed;
        snowColumns = columns;
    }

    private void ResizeSnowMask()
    {
        int targetRows = TargetSnowRows();
        int targetColumns = TargetSnowColumns();
        byte fill = snowPacked == "0" ? (byte)0 : (byte)255;
        if (snowMask == null)
        {
            snowColumns = targetColumns;
            snowMask = NewSnowMask(targetRows, targetColumns, fill);
            snowRemainder = null;
            return;
        }

        int columns = Mathf.Max(snowColumns, 2);
        if (snowMask.Length % columns != 0)
        {
            snowColumns = targetColumns;
            snowMask = NewSnowMask(targetRows, targetColumns, fill);
            snowRemainder = null;
            return;
        }

        int rows = snowMask.Length / columns;
        if (rows < 2)
        {
            snowColumns = targetColumns;
            snowMask = NewSnowMask(targetRows, targetColumns, fill);
            snowRemainder = null;
            return;
        }

        if (columns != targetColumns)
        {
            var widened = new byte[rows * targetColumns];
            ResampleSnowWidth(snowMask, rows, columns, widened, targetColumns);
            snowMask = widened;
            snowColumns = targetColumns;
            columns = targetColumns;
            snowRemainder = null;
        }

        int gap = Mathf.Abs(rows - targetRows);
        if (gap < Mathf.Max(4, targetRows / 12))
        {
            return;
        }

        var resampled = new byte[targetRows * columns];
        ResampleSnow(snowMask, rows, resampled, targetRows, columns);
        snowMask = resampled;
        snowRemainder = null;
    }

    private static byte[] NewSnowMask(int rows, int columns, byte value)
    {
        var mask = new byte[rows * columns];
        if (value == 0)
        {
            return mask;
        }

        for (int i = 0; i < mask.Length; i++)
        {
            mask[i] = value;
        }

        return mask;
    }

    private static void ResampleSnow(byte[] source, int sourceRows, byte[] dest, int destRows, int columns)
    {
        for (int row = 0; row < destRows; row++)
        {
            float t = destRows <= 1 ? 0f : row / (float)(destRows - 1);
            float src = t * (sourceRows - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(src), 0, sourceRows - 1);
            int y1 = Mathf.Min(y0 + 1, sourceRows - 1);
            float blendY = src - y0;
            for (int column = 0; column < columns; column++)
            {
                float a = source[y0 * columns + column];
                float b = source[y1 * columns + column];
                dest[row * columns + column] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(a, b, blendY)), 0, 255);
            }
        }
    }

    private static void ResampleSnowWidth(byte[] source, int rows, int sourceColumns, byte[] dest, int destColumns)
    {
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < destColumns; column++)
            {
                float x = destColumns <= 1 ? 0f : column / (float)(destColumns - 1) * (sourceColumns - 1);
                int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, sourceColumns - 1);
                int x1 = Mathf.Min(x0 + 1, sourceColumns - 1);
                float blendX = x - x0;
                float a = source[row * sourceColumns + x0];
                float b = source[row * sourceColumns + x1];
                dest[row * destColumns + column] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(a, b, blendX)), 0, 255);
            }
        }
    }

    private void ApplySnowColors()
    {
        // The shader reads the mask texture. Painting vertex colors on the ribbon does not show up.
    }

    private void ResizeSnowTexture()
    {
        int columns = Mathf.Max(snowColumns, 2);
        int rows = TargetSnowRows();
        if (snowMask != null && snowMask.Length >= columns * 2 && snowMask.Length % columns == 0)
        {
            rows = snowMask.Length / columns;
        }

        if (snowTexture != null && snowTexture.width == columns && snowTexture.height == rows)
        {
            return;
        }

        ReleaseSnowTexture();
        // Mipmaps keep distant fragments off the full-resolution mask. The trail is a thin line,
        // so incremental uploads only refresh mip 0 and the coarser levels stay snow-white.
        snowTexture = new Texture2D(columns, rows, TextureFormat.R8, true, true)
        {
            name = "RoadSnowMask",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 0,
            hideFlags = HideFlags.HideAndDontSave
        };
        snowUploadFull = true;
        snowTextureDirty = true;
    }

    private void MarkSnowDirty(int column, int row)
    {
        if (!snowHasDirtyRect)
        {
            dirtyColMin = dirtyColMax = column;
            dirtyRowMin = dirtyRowMax = row;
            snowHasDirtyRect = true;
            return;
        }

        if (column < dirtyColMin)
        {
            dirtyColMin = column;
        }

        if (column > dirtyColMax)
        {
            dirtyColMax = column;
        }

        if (row < dirtyRowMin)
        {
            dirtyRowMin = row;
        }

        if (row > dirtyRowMax)
        {
            dirtyRowMax = row;
        }
    }

    private void FlushSnowTexture()
    {
        ResizeSnowTexture();
        if (snowTexture == null)
        {
            return;
        }

        if (snowUploadFull || !snowHasDirtyRect || !TryUploadSnowRect())
        {
            UploadFullSnowTexture();
        }

        // One shared material keeps the chunks on the SRP batcher. A property block would split them.
        Chunks.RefreshMaterial(roadMaterial);
        if (Chunks.Material != null)
        {
            Chunks.Material.SetTexture("_SnowMap", snowTexture);
        }

        snowTextureDirty = false;
        snowUploadFull = false;
        snowHasDirtyRect = false;
    }

    private void UploadFullSnowTexture()
    {
        int size = snowTexture.width * snowTexture.height;
        byte[] pixels;
        if (snowMask != null && snowMask.Length == size)
        {
            pixels = snowMask;
        }
        else
        {
            pixels = new byte[size];
            if (snowPacked != "0")
            {
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = 255;
                }
            }
        }

        snowTexture.SetPixelData(pixels, 0);
        snowTexture.Apply(true, false);
    }

    private bool TryUploadSnowRect()
    {
        if (snowMask == null || snowTexture == null)
        {
            return false;
        }

        if ((SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) == 0)
        {
            return false;
        }

        int columns = snowTexture.width;
        int rows = snowTexture.height;
        if (snowMask.Length != columns * rows)
        {
            return false;
        }

        int columnMin = Mathf.Clamp(dirtyColMin, 0, columns - 1);
        int columnMax = Mathf.Clamp(dirtyColMax, 0, columns - 1);
        int rowMin = Mathf.Clamp(dirtyRowMin, 0, rows - 1);
        int rowMax = Mathf.Clamp(dirtyRowMax, 0, rows - 1);
        if (columnMax < columnMin || rowMax < rowMin)
        {
            return false;
        }

        // One patch is 128 texels. A longer trail is copied in tiles so a wide rect
        // does not rebuild every mip of the full mask.
        ResizeSnowPatch();
        int stride = SnowPatchExtent;
        for (int row0 = rowMin; row0 <= rowMax; row0 += SnowPatchExtent)
        {
            int height = Mathf.Min(SnowPatchExtent, rowMax - row0 + 1);
            for (int col0 = columnMin; col0 <= columnMax; col0 += SnowPatchExtent)
            {
                int width = Mathf.Min(SnowPatchExtent, columnMax - col0 + 1);
                for (int row = 0; row < height; row++)
                {
                    int source = (row + row0) * columns + col0;
                    int dest = row * stride;
                    System.Array.Copy(snowMask, source, snowPatchBytes, dest, width);
                }

                snowPatch.SetPixelData(snowPatchBytes, 0);
                snowPatch.Apply(false, false);
                Graphics.CopyTexture(snowPatch, 0, 0, 0, 0, width, height, snowTexture, 0, 0, col0, row0);
            }
        }

        return true;
    }

    private void ResizeSnowPatch()
    {
        int size = SnowPatchExtent * SnowPatchExtent;
        if (snowPatchBytes == null || snowPatchBytes.Length != size)
        {
            snowPatchBytes = new byte[size];
        }

        if (snowPatch != null && snowPatch.width == SnowPatchExtent && snowPatch.height == SnowPatchExtent)
        {
            return;
        }

        ReleaseTexture(ref snowPatch);
        snowPatch = new Texture2D(SnowPatchExtent, SnowPatchExtent, TextureFormat.R8, false, true)
        {
            name = "RoadSnowPatch",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    private void ReleaseSnowTexture()
    {
        ReleaseTexture(ref snowTexture);
        ReleaseTexture(ref snowPatch);
    }

    private static void ReleaseTexture(ref Texture2D texture)
    {
        if (texture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(texture);
        }
        else
        {
            DestroyImmediate(texture);
        }

        texture = null;
    }

    private void PersistSnow()
    {
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this))
        {
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }

        if (gameObject.scene.IsValid())
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif
    }

    /// <summary>
    /// Rebuilds every mesh from the current spline. The collider and the walls stay on a coarse ribbon.
    /// The snow surface is a finer ribbon, split into chunks, and this object stops drawing so the coarse
    /// mesh is not shaded a second time.
    /// </summary>
    private void Rebuild()
    {
        if (splineContainer == null || splineContainer.Spline == null || splineContainer.Spline.Count < 2)
        {
            return;
        }

        var spline = splineContainer.Spline;
        float length = Mathf.Max(spline.GetLength(), 0.01f);
        // The cached length belonged to the spline that is about to be resampled.
        InvalidateRoadLength();
        ResizeSculptGrid();

        // Physics keeps a coarse ribbon. A vertex every fifth of a meter on a 4 km track is wasted contact work.
        int colliderSegments = Mathf.Clamp(Mathf.Max(Mathf.CeilToInt(length * segmentsPerMeter), SculptRows - 1), 2, 400);
        BuildSurface(spline, length, colliderSegments, Columns, out Vector3[] colliderVertices, out Vector2[] colliderUvs, out Vector2[] colliderParametric, out int[] colliderTriangles, out Vector3[] colliderUps);
        RoadMeshBuilder.UploadMesh(ref roadMesh, "SplineRoad", colliderVertices, colliderUvs, colliderParametric, colliderTriangles, padBounds: false);

        // The drawn surface is denser than the collider, and both axes are clamped so a long road stays bounded.
        int visualAcross = Mathf.Clamp(Mathf.RoundToInt(width / VisualVertexSpacing) + 1, Columns, MaxVisualColumns);
        int visualSegments = Mathf.Clamp(Mathf.CeilToInt(length / VisualVertexSpacing), colliderSegments, MaxVisualSegments);
        BuildSurface(spline, length, visualSegments, visualAcross, out Vector3[] vertices, out Vector2[] uvs, out Vector2[] parametric, out int[] triangles, out _);
        var normals = new Vector3[vertices.Length];
        // Normals are accumulated on the full ribbon before the split, so chunk edges share one normal.
        RoadMeshBuilder.AccumulateNormals(vertices, triangles, normals);
        Chunks.Build(roadMaterial, vertices, uvs, parametric, normals, triangles, visualSegments, visualAcross);

        // The collider mesh stays on this object. Drawing it as well would shade the whole track again.
        var filter = GetComponent<MeshFilter>();
        filter.sharedMesh = null;
        var roadRenderer = GetComponent<MeshRenderer>();
        if (roadRenderer != null)
        {
            roadRenderer.enabled = false;
        }

        var collider = GetComponent<MeshCollider>();
        if (collider != null)
        {
            collider.convex = false;
            collider.sharedMesh = null;
            collider.sharedMesh = roadMesh;
            collider.sharedMaterial = RoadPhysics();
        }

        // Walls stay on the coarse mesh. The snow shader fades to zero at the edge, so they still meet the road.
        Walls.Build(wallHeight, colliderVertices, colliderUps, Columns, colliderSegments);
        if (debugShowChunks)
        {
            Chunks.SetDebugVisible(true);
        }
    }

    /// <summary>
    /// Builds one road ribbon in the road's local space. The collider and the visual surface both come from here.
    /// This only fills arrays. The caller uploads them to a mesh.
    /// </summary>
    /// <param name="spline">Curve the ribbon follows. Positions it returns are already in the road's local space.</param>
    /// <param name="length">Spline length in meters. Scales how fast the road albedo repeats along the track.</param>
    /// <param name="segments">Quad count along the spline. The ribbon has <paramref name="segments"/> + 1 vertex rings.</param>
    /// <param name="across">Vertex count across the road, including both edges.</param>
    /// <param name="vertices">Road-local positions, ring by ring from the start of the spline, <paramref name="across"/> vertices per ring.</param>
    /// <param name="uvs">Albedo texture coordinates, one per vertex. x runs across the road, y runs along it.</param>
    /// <param name="parametric">Spline coordinates per vertex. x is 0 at the left edge and 1 at the right. y is 0 at the start and 1 at the end. The snow mask reads this from uv2.</param>
    /// <param name="triangles">Triangle indices into <paramref name="vertices"/>, six per quad. Wound so the front face points along the spline up.</param>
    /// <param name="ups">Spline up at each ring, one per ring. Walls extrude from the road edge along this direction.</param>
    private void BuildSurface(Spline spline, float length, int segments, int across, out Vector3[] vertices, out Vector2[] uvs, out Vector2[] parametric, out int[] triangles, out Vector3[] ups)
    {
        float halfWidth = width * 0.5f;
        // A segment joins two rings, so the last ring sits one past the last segment.
        int rings = segments + 1;
        vertices = new Vector3[rings * across];
        uvs = new Vector2[vertices.Length];
        parametric = new Vector2[vertices.Length];
        triangles = new int[segments * (across - 1) * 6];
        ups = new Vector3[rings];

        float3 upAtStart = new float3(0f, 1f, 0f);

        for (int i = 0; i < rings; i++)
        {
            float t = i / (float)segments;
            SplineUtility.Evaluate(spline, t, out float3 position, out float3 tangent, out float3 up);
            if (i == 0)
            {
                upAtStart = up;
            }

            Vector3 upDirection = (Vector3)up;
            ups[i] = upDirection.sqrMagnitude > 0.0001f ? upDirection.normalized : Vector3.up;

            // Side points from the center toward the right edge. A zero tangent falls back to world X.
            float3 side = math.normalizesafe(math.cross(up, tangent), new float3(1f, 0f, 0f));
            float bend = ProfileAt(t);

            for (int c = 0; c < across; c++)
            {
                float u = c / (float)(across - 1);
                float centered = u * 2f - 1f;
                // Parabola: edges stay on the spline, the center rises or drops.
                float lift = bend * (1f - centered * centered) + SampleSculpt(t, u);
                float3 point = position + side * (centered * halfWidth) + up * lift;

                int index = i * across + c;
                vertices[index] = point;
                // 0.05 puts one albedo repeat every 20 m, so the texture does not stretch over the whole track.
                uvs[index] = new Vector2(u, t * length * 0.05f);
                parametric[index] = new Vector2(u, t);
            }
        }

        int triangle = 0;
        for (int i = 0; i < segments; i++)
        {
            for (int c = 0; c < across - 1; c++)
            {
                int bottomLeft = i * across + c;
                int bottomRight = bottomLeft + 1;
                int topLeft = bottomLeft + across;
                int topRight = topLeft + 1;
                triangles[triangle++] = bottomLeft;
                triangles[triangle++] = topLeft;
                triangles[triangle++] = bottomRight;
                triangles[triangle++] = bottomRight;
                triangles[triangle++] = topLeft;
                triangles[triangle++] = topRight;
            }
        }

        // One winding decision for the whole ribbon, taken from the first quad against the up at the start.
        // A bank later in the track does not flip triangles on its own.
        float3 edge = (float3)vertices[across] - (float3)vertices[0];
        float3 span = (float3)vertices[1] - (float3)vertices[0];
        if (math.dot(math.cross(edge, span), upAtStart) < 0f)
        {
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int swap = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = swap;
            }
        }
    }

    private PhysicsMaterial RoadPhysics()
    {
        if (roadPhysics == null)
        {
            roadPhysics = new PhysicsMaterial("RoadFriction")
            {
                hideFlags = HideFlags.HideAndDontSave,
                bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum,
                frictionCombine = PhysicsMaterialCombine.Maximum,
            };
        }

        float contactFriction = Mathf.Max(0f, friction);
        roadPhysics.dynamicFriction = contactFriction;
        roadPhysics.staticFriction = contactFriction;
        return roadPhysics;
    }

    private void OnValidate()
    {
        width = Mathf.Max(1f, width);
        segmentsPerMeter = Mathf.Clamp(segmentsPerMeter, 0.05f, 2f);
        // Recoloring chunks must not rebuild the 4 km ribbon.
        if (debugShowChunks != debugShowChunksCached)
        {
            debugShowChunksCached = debugShowChunks;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled)
                {
                    Chunks.SetDebugVisible(debugShowChunks);
                }
            };
#endif
            return;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled)
            {
                Rebuild();
            }
        };
#endif
    }
}
