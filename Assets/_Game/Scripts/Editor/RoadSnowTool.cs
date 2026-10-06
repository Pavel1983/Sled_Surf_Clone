using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Scene tool that clears or restores snow on a <see cref="SplineRoad"/>.
/// </summary>
[EditorTool("Road Snow", typeof(SplineRoad))]
public class RoadSnowTool : EditorTool
{
    private const string RadiusKey = "SledSurf.RoadSnow.Radius";
    private const string StrengthKey = "SledSurf.RoadSnow.CoveragePerSecond";
    private const string RestoreKey = "SledSurf.RoadSnow.Restore";
    private static Material brushMaterial;

    private bool painting;
    private bool undoCaptured;
    private double lastApplyTime;

    private static float BrushRadius
    {
        get => EditorPrefs.GetFloat(RadiusKey, 5f);
        set => EditorPrefs.SetFloat(RadiusKey, value);
    }

    private static float BrushStrength
    {
        get => EditorPrefs.GetFloat(StrengthKey, 2f);
        set => EditorPrefs.SetFloat(StrengthKey, value);
    }

    private static bool RestoreMode
    {
        get => EditorPrefs.GetBool(RestoreKey, false);
        set => EditorPrefs.SetBool(RestoreKey, value);
    }

    public override void OnActivated()
    {
        EditorApplication.update += TickWhilePainting;
    }

    public override void OnWillBeDeactivated()
    {
        EditorApplication.update -= TickWhilePainting;
        painting = false;
    }

    public override void OnToolGUI(EditorWindow window)
    {
        var road = target as SplineRoad;
        if (road == null)
        {
            return;
        }

        Event current = Event.current;
        Rect panel = new Rect(12f, 12f, 250f, 188f);
        bool overPanel = panel.Contains(current.mousePosition);

        if (current.type == EventType.MouseUp && current.button == 0)
        {
            if (undoCaptured)
            {
                road.CommitSnow();
            }

            painting = false;
            undoCaptured = false;
        }

        if (current.type == EventType.Layout && !overPanel)
        {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        }

        DrawPanel(road, panel);
        if (overPanel || current.alt)
        {
            return;
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
        bool hit = road.TryRaycast(ray, 8000f, out SplineRoad.RoadSurfaceHit surface);
        bool restoring = EffectiveRestore(current);

        if (current.type == EventType.Repaint && hit)
        {
            DrawBrush(road, surface, restoring);
        }

        if (current.type == EventType.MouseMove)
        {
            window.Repaint();
        }

        if (current.type == EventType.MouseDown && current.button == 0 && hit)
        {
            Undo.RecordObject(road, "Paint Road Snow");
            undoCaptured = true;
            painting = true;
            lastApplyTime = EditorApplication.timeSinceStartup;
            Apply(road, surface, current, 1f / 30f);
            current.Use();
            window.Repaint();
        }
        else if (current.type == EventType.MouseDrag && current.button == 0 && hit)
        {
            if (!undoCaptured)
            {
                Undo.RecordObject(road, "Paint Road Snow");
                undoCaptured = true;
            }

            painting = true;
            current.Use();
        }

        if (current.type == EventType.Repaint && painting && hit)
        {
            double now = EditorApplication.timeSinceStartup;
            float deltaTime = Mathf.Clamp((float)(now - lastApplyTime), 0f, 0.05f);
            lastApplyTime = now;
            if (deltaTime > 0f)
            {
                Apply(road, surface, current, deltaTime);
            }
        }
    }

    private void TickWhilePainting()
    {
        if (painting)
        {
            SceneView.RepaintAll();
        }
    }

    private static bool EffectiveRestore(Event current)
    {
        return RestoreMode ^ current.shift;
    }

    private static float SignedCoverage(Event current, float seconds)
    {
        float sign = EffectiveRestore(current) ? 1f : -1f;
        return sign * BrushStrength * seconds;
    }

    private static void Apply(SplineRoad road, SplineRoad.RoadSurfaceHit surface, Event current, float seconds)
    {
        road.PaintSnow(surface.u, surface.t, BrushRadius, SignedCoverage(current, seconds));
    }

    private static void DrawPanel(SplineRoad road, Rect panel)
    {
        Handles.BeginGUI();
        GUILayout.BeginArea(panel, EditorStyles.helpBox);
        GUILayout.Label("Road Snow");
        BrushRadius = Mathf.Max(0.01f, EditorGUILayout.FloatField("Size", BrushRadius));
        BrushStrength = Mathf.Max(0f, EditorGUILayout.FloatField("Strength (1/s)", BrushStrength));
        RestoreMode = GUILayout.Toolbar(RestoreMode ? 1 : 0, new[] { "Clear", "Restore" }) == 1;
        GUILayout.Label("Shift swaps clear and restore.");
        if (GUILayout.Button("Fill snow"))
        {
            Undo.RecordObject(road, "Fill Road Snow");
            road.FillSnow();
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("Clear all snow"))
        {
            Undo.RecordObject(road, "Clear Road Snow");
            road.ClearAllSnow();
            SceneView.RepaintAll();
        }

        GUILayout.EndArea();
        Handles.EndGUI();
    }

    private static void DrawBrush(SplineRoad road, SplineRoad.RoadSurfaceHit surface, bool restoring)
    {
        Vector3 normal = surface.normal.sqrMagnitude > 0.001f ? surface.normal.normalized : Vector3.up;
        Vector3 tangent = Vector3.Cross(normal, Vector3.up);
        if (tangent.sqrMagnitude < 0.001f)
        {
            tangent = Vector3.Cross(normal, Vector3.right);
        }

        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(normal, tangent);

        const int count = 48;
        float radius = BrushRadius;
        var ring = new Vector3[count];
        var collider = road.GetComponent<MeshCollider>();

        for (int i = 0; i < count; i++)
        {
            float angle = i / (float)count * Mathf.PI * 2f;
            Vector3 probe = surface.point + (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * radius;
            Vector3 snapped = probe;
            if (collider != null && collider.Raycast(new Ray(probe + normal * (radius + 4f), -normal), out RaycastHit edgeHit, radius + 8f))
            {
                snapped = edgeHit.point;
            }

            ring[i] = snapped + normal * 0.12f;
        }

        Color fill = restoring
            ? new Color(0.95f, 0.97f, 1f, 0.4f)
            : new Color(0.35f, 0.7f, 1f, 0.35f);
        Color outline = restoring
            ? new Color(1f, 1f, 1f, 1f)
            : new Color(0.55f, 0.85f, 1f, 1f);

        DrawSpot(surface.point + normal * 0.14f, ring, fill);
        Handles.color = outline;
        var closed = new Vector3[count + 1];
        for (int i = 0; i < count; i++)
        {
            closed[i] = ring[i];
        }

        closed[count] = ring[0];
        Handles.DrawAAPolyLine(3f, closed);
    }

    private static void DrawSpot(Vector3 center, Vector3[] ring, Color color)
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        if (brushMaterial == null)
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                return;
            }

            brushMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        brushMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        brushMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        brushMaterial.SetInt("_Cull", (int)CullMode.Off);
        brushMaterial.SetInt("_ZWrite", 0);
        brushMaterial.SetInt("_ZTest", (int)CompareFunction.LessEqual);
        brushMaterial.SetPass(0);

        GL.PushMatrix();
        GL.MultMatrix(Handles.matrix);
        GL.Begin(GL.TRIANGLES);
        GL.Color(color);
        for (int i = 0; i < ring.Length; i++)
        {
            Vector3 next = ring[(i + 1) % ring.Length];
            GL.Vertex(center);
            GL.Vertex(ring[i]);
            GL.Vertex(next);
        }

        GL.End();
        GL.PopMatrix();
    }
}

/// <summary>
/// Same snow tool, shown while the Spline tool context is active.
/// </summary>
[EditorTool("Road Snow", typeof(SplineRoad), typeof(SplineToolContext))]
public class RoadSnowSplineTool : RoadSnowTool
{
}
