using System;
using UnityEngine;

/// <summary>
/// A see-through wall across the whole road at one distance from the start: the player's record, or the finish.
/// It shows up when the sled gets near, and when the sled passes it the wall is gone and a fanfare of confetti goes off.
/// The wall is a strip that follows the road surface from edge to edge, so it also stands right on banks and bumps.
/// </summary>
[DisallowMultipleComponent]
public class DistanceGate : MonoBehaviour
{
    // How far the wall reaches under the surface, in meters, so no gap shows on a rough road.
    private const float Sink = 1f;

    /// <summary>
    /// Raised once when the sled passes the wall.
    /// </summary>
    public event Action Crossed;

    [SerializeField] private Sled sled;
    [SerializeField] private SplineRoad road;
    [Tooltip("The wall. Its mesh is rebuilt every time the gate is placed.")]
    [SerializeField] private MeshFilter wall;
    [Tooltip("Confetti that goes off where the sled crosses the wall.")]
    [SerializeField] private ParticleSystem fanfare;
    [Tooltip("Height of the wall above the road, in meters.")]
    [SerializeField] private float height = 14f;
    [Tooltip("The wall appears when the sled is closer than this, in meters along the road.")]
    [SerializeField] private float visibleRange = 400f;
    [Tooltip("How many meters across the road one repeat of the wall texture covers. Up the wall the texture is shown once.")]
    [SerializeField] private float textureMeters = 6f;
    [Tooltip("Quads across the road. More follow a bumpy cross-section closer.")]
    [SerializeField] private int segments = 32;

    private Mesh mesh;
    private float gateMeters;
    private bool placed;

    private void LateUpdate()
    {
        if (!placed)
        {
            return;
        }

        float ahead = gateMeters - sled.DistanceMeters;
        if (sled.IsRiding && ahead <= 0f)
        {
            Cross();
            return;
        }

        bool visible = ahead <= visibleRange;
        if (wall.gameObject.activeSelf != visible)
        {
            wall.gameObject.SetActive(visible);
        }
    }

    private void OnDestroy()
    {
        if (mesh != null)
        {
            Destroy(mesh);
        }
    }

    /// <summary>
    /// Stands the wall across the road at this distance from the start and arms it for the next run.
    /// </summary>
    public void Place(float distanceMeters)
    {
        gateMeters = distanceMeters;
        BuildWall(distanceMeters / road.RoadLength());
        wall.gameObject.SetActive(false);
        placed = true;
    }

    /// <summary>Takes the wall away, for a run that has no such mark.</summary>
    public void Remove()
    {
        placed = false;
        wall.gameObject.SetActive(false);
    }

    private void Cross()
    {
        placed = false;
        wall.gameObject.SetActive(false);
        fanfare.transform.position = sled.Body.position;
        fanfare.Play();
        Crossed?.Invoke();
    }

    private void BuildWall(float t)
    {
        int columns = Mathf.Max(1, segments) + 1;
        var vertices = new Vector3[columns * 2];
        var uvs = new Vector2[columns * 2];
        var triangles = new int[(columns - 1) * 6];
        Transform frame = wall.transform;
        float across = 0f;
        Vector3 previous = Vector3.zero;
        for (int i = 0; i < columns; i++)
        {
            road.TryGetSurface(t, i / (float)(columns - 1), out Vector3 point, out _, out Vector3 up);
            if (i > 0)
            {
                across += Vector3.Distance(previous, point);
            }

            previous = point;
            // Across the road the texture repeats by the meter, so it keeps its size on a road of any width.
            // Up the wall it runs once, from the ground to the top edge.
            float textureX = across / textureMeters;
            vertices[i * 2] = frame.InverseTransformPoint(point - up * Sink);
            vertices[i * 2 + 1] = frame.InverseTransformPoint(point + up * height);
            uvs[i * 2] = new Vector2(textureX, 0f);
            uvs[i * 2 + 1] = new Vector2(textureX, 1f);
        }

        for (int i = 0; i < columns - 1; i++)
        {
            int bottomLeft = i * 2;
            int topLeft = bottomLeft + 1;
            int bottomRight = bottomLeft + 2;
            int topRight = bottomLeft + 3;
            int index = i * 6;
            triangles[index] = bottomLeft;
            triangles[index + 1] = topLeft;
            triangles[index + 2] = bottomRight;
            triangles[index + 3] = bottomRight;
            triangles[index + 4] = topLeft;
            triangles[index + 5] = topRight;
        }

        if (mesh == null)
        {
            mesh = new Mesh { name = "DistanceGate", hideFlags = HideFlags.HideAndDontSave };
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        wall.sharedMesh = mesh;
    }
}
