using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Scene tool that raises or lowers a <see cref="SplineRoad"/> under a round brush.
/// </summary>
[EditorTool("Road Sculpt", typeof(SplineRoad))]
public class RoadSculptTool : EditorTool
{
    private const string RadiusKey = "SledSurf.RoadSculpt.Radius";
    private const string StrengthKey = "SledSurf.RoadSculpt.MetersPerSecond";
    private const string LowerKey = "SledSurf.RoadSculpt.Lower";
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
        get => EditorPrefs.GetFloat(StrengthKey, 3f);
        set => EditorPrefs.SetFloat(StrengthKey, value);
    }

    private static bool LowerMode
    {
        get => EditorPrefs.GetBool(LowerKey, false);
        set => EditorPrefs.SetBool(LowerKey, value);
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
        Rect panel = new Rect(12f, 12f, 250f, 156f);
        bool overPanel = panel.Contains(current.mousePosition);

        if (current.type == EventType.MouseUp && current.button == 0)
        {
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
        bool lowering = EffectiveLower(current);

        if (current.type == EventType.Repaint && hit)
        {
            DrawBrush(road, surface, lowering);
        }

        if (current.type == EventType.MouseMove)
        {
            window.Repaint();
        }

        if (current.type == EventType.MouseDown && current.button == 0 && hit)
        {
            Undo.RecordObject(road, "Sculpt Road");
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
                Undo.RecordObject(road, "Sculpt Road");
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

    private static bool EffectiveLower(Event current)
    {
        return LowerMode ^ current.shift;
    }

    private static float SignedStrength(Event current, float seconds)
    {
        float sign = EffectiveLower(current) ? -1f : 1f;
        return sign * BrushStrength * seconds;
    }

    private static void Apply(SplineRoad road, SplineRoad.RoadSurfaceHit surface, Event current, float seconds)
    {
        road.SculptStroke(surface.point, BrushRadius, SignedStrength(current, seconds));
    }

    private static void DrawPanel(SplineRoad road, Rect panel)
    {
        Handles.BeginGUI();
        GUILayout.BeginArea(panel, EditorStyles.helpBox);
        GUILayout.Label("Road Sculpt");
        BrushRadius = Mathf.Max(0.01f, EditorGUILayout.FloatField("Size", BrushRadius));
        BrushStrength = Mathf.Max(0f, EditorGUILayout.FloatField("Strength (m/s)", BrushStrength));
        LowerMode = GUILayout.Toolbar(LowerMode ? 1 : 0, new[] { "Raise", "Lower" }) == 1;
        GUILayout.Label("Shift swaps raise and lower.");
        if (GUILayout.Button("Clear sculpt"))
        {
            Undo.RecordObject(road, "Clear Road Sculpt");
            road.ClearSculpt();
        }

        GUILayout.EndArea();
        Handles.EndGUI();
    }

    private static void DrawBrush(SplineRoad road, SplineRoad.RoadSurfaceHit surface, bool lowering)
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

        Color fill = lowering
            ? new Color(1f, 0.35f, 0.22f, 0.33f)
            : new Color(0.25f, 0.82f, 1f, 0.33f);
        Color outline = lowering
            ? new Color(1f, 0.55f, 0.35f, 1f)
            : new Color(0.9f, 1f, 1f, 1f);

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
/// Same sculpt tool, shown while the Spline tool context is active.
/// </summary>
[EditorTool("Road Sculpt", typeof(SplineRoad), typeof(SplineToolContext))]
public class RoadSculptSplineTool : RoadSculptTool
{
}
