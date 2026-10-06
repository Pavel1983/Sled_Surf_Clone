using UnityEditor;
using UnityEngine;

/// <summary>
/// Seats the selected objects on a <see cref="SplineRoad"/> and tilts them to the surface.
/// Place props by hand first, then select the ones that need fixing and run a command.
/// </summary>
public class RoadSnapWindow : EditorWindow
{
    private const string SinkKey = "SledSurf.RoadSnap.SinkMeters";
    private const string BottomKey = "SledSurf.RoadSnap.SeatByBottom";

    // Sculpted lips rise well over a hundred meters, so the probe starts above any of them.
    private const float ProbeHeight = 300f;

    // An object this far outside the road edge is not on the road.
    private const float EdgeToleranceMeters = 2f;

    private static float SinkMeters
    {
        get => EditorPrefs.GetFloat(SinkKey, 0f);
        set => EditorPrefs.SetFloat(SinkKey, value);
    }

    private static bool SeatByBottom
    {
        get => EditorPrefs.GetBool(BottomKey, false);
        set => EditorPrefs.SetBool(BottomKey, value);
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Select objects in the scene, then snap them. Both commands seat the object on the road and tilt it to the surface.",
            MessageType.None);

        SinkMeters = EditorGUILayout.FloatField(
            new GUIContent("Sink (m)", "How deep the object is pushed into the snow. Negative lifts it."),
            SinkMeters);
        SeatByBottom = EditorGUILayout.Toggle(
            new GUIContent("Seat By Mesh Bottom", "Puts the lowest point of the meshes on the road. Turn on for models whose pivot is not at the base."),
            SeatByBottom);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(Selection.transforms.Length == 0))
        {
            if (GUILayout.Button(new GUIContent("Snap, Keep Facing", "Keeps the way the object is turned.")))
            {
                Snap(false);
            }

            if (GUILayout.Button(new GUIContent("Snap, Face Along Road", "Turns the object's forward axis down the road, so a wide prop lies across it.")))
            {
                Snap(true);
            }
        }
    }

    private void OnSelectionChange()
    {
        Repaint();
    }

    [MenuItem("Tools/Road/Snap Settings")]
    private static void Open()
    {
        GetWindow<RoadSnapWindow>("Road Snap");
    }

    [MenuItem("Tools/Road/Snap Selection To Road (Keep Facing) %&r")]
    private static void SnapKeepFacing()
    {
        Snap(false);
    }

    [MenuItem("Tools/Road/Snap Selection To Road (Face Along Road) %&#r")]
    private static void SnapAlongRoad()
    {
        Snap(true);
    }

    [MenuItem("Tools/Road/Snap Selection To Road (Keep Facing) %&r", true)]
    [MenuItem("Tools/Road/Snap Selection To Road (Face Along Road) %&#r", true)]
    private static bool CanSnap()
    {
        return Selection.transforms.Length > 0;
    }

    private static void Snap(bool faceAlongRoad)
    {
        SplineRoad road = FindFirstObjectByType<SplineRoad>();
        if (road == null)
        {
            Debug.LogWarning("Road Snap: there is no SplineRoad in the open scene.");
            return;
        }

        // Top level only. A child moves with its parent, and snapping both would move it twice.
        Transform[] targets = Selection.GetTransforms(SelectionMode.TopLevel | SelectionMode.Editable);
        if (targets.Length == 0)
        {
            return;
        }

        Undo.RecordObjects(targets, "Snap To Road");
        int snapped = 0;
        for (int i = 0; i < targets.Length; i++)
        {
            Transform target = targets[i];
            if (target.GetComponentInParent<SplineRoad>() != null)
            {
                continue;
            }

            if (!TryFindSurface(road, target.position, out Vector3 point, out Vector3 normal, out Vector3 tangent))
            {
                Debug.LogWarning($"Road Snap: '{target.name}' is not over the road, skipped.", target);
                continue;
            }

            Vector3 heading = faceAlongRoad ? tangent : target.forward;
            Vector3 forward = Vector3.ProjectOnPlane(heading, normal);
            // An object that points straight into the surface has no heading left on it.
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.ProjectOnPlane(tangent, normal);
            }

            if (forward.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            target.rotation = Quaternion.LookRotation(forward.normalized, normal);
            target.position = point;
            float lift = SeatByBottom ? -LowestPoint(target, point, normal) : 0f;
            target.position = point + normal * (lift - SinkMeters);
            snapped++;
        }

        if (snapped < targets.Length)
        {
            Debug.Log($"Road Snap: snapped {snapped} of {targets.Length} objects.");
        }
    }

    private static bool TryFindSurface(SplineRoad road, Vector3 position, out Vector3 point, out Vector3 normal, out Vector3 tangent)
    {
        point = position;
        normal = Vector3.up;
        if (!road.TryGetSurfaceCoord(position, out float t, out float u, out tangent))
        {
            return false;
        }

        if (!road.TryGetSurface(t, u, out Vector3 surface, out _, out Vector3 up))
        {
            return false;
        }

        // The coordinate is clamped to the ribbon. A point beside the road still gets the nearest
        // edge, so compare where it landed with where the object really is.
        Vector3 offset = Vector3.ProjectOnPlane(position - surface, up);
        if (offset.magnitude > EdgeToleranceMeters)
        {
            return false;
        }

        point = surface;
        normal = up;
        // The collider is what the sled rides, and its normal follows the bowl and the sculpted
        // relief. The spline's own up vector does not.
        var probe = new Ray(surface + up * ProbeHeight, -up);
        if (road.TryRaycast(probe, ProbeHeight * 2f, out SplineRoad.RoadSurfaceHit hit) && hit.normal.sqrMagnitude > 0.0001f)
        {
            point = hit.point;
            normal = hit.normal.normalized;
        }

        return true;
    }

    /// <summary>
    /// Height of the lowest mesh corner above the surface point, measured along the normal.
    /// Negative when the model reaches below its pivot.
    /// </summary>
    private static float LowestPoint(Transform target, Vector3 point, Vector3 normal)
    {
        float lowest = float.PositiveInfinity;
        MeshFilter[] filters = target.GetComponentsInChildren<MeshFilter>();
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i].sharedMesh != null)
            {
                lowest = Mathf.Min(lowest, LowestCorner(filters[i].transform, filters[i].sharedMesh.bounds, point, normal));
            }
        }

        SkinnedMeshRenderer[] skins = target.GetComponentsInChildren<SkinnedMeshRenderer>();
        for (int i = 0; i < skins.Length; i++)
        {
            lowest = Mathf.Min(lowest, LowestCorner(skins[i].transform, skins[i].localBounds, point, normal));
        }

        return float.IsPositiveInfinity(lowest) ? 0f : lowest;
    }

    private static float LowestCorner(Transform owner, Bounds bounds, Vector3 point, Vector3 normal)
    {
        float lowest = float.PositiveInfinity;
        for (int corner = 0; corner < 8; corner++)
        {
            var local = new Vector3(
                (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
            lowest = Mathf.Min(lowest, Vector3.Dot(owner.TransformPoint(local) - point, normal));
        }

        return lowest;
    }
}
