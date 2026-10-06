using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Thick circular arc for the tension meter and the speed gauge.
/// 0° points right and positive degrees run counter-clockwise. A negative sweep runs clockwise.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class UiArc : MaskableGraphic
{
    private const float Feather = 1.35f;

    [SerializeField] private float startAngle = 180f;
    [SerializeField] private float sweepAngle = -180f;
    [SerializeField] private float thickness = 36f;
    [SerializeField, Range(0f, 1f)] private float fill = 1f;
    [SerializeField] private bool roundedCaps = true;

    public float Fill
    {
        get => fill;
        set
        {
            float clamped = Mathf.Clamp01(value);
            if (Mathf.Abs(fill - clamped) < 0.0004f)
            {
                return;
            }

            fill = clamped;
            SetVerticesDirty();
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = rectTransform.rect;
        float limit = Mathf.Min(rect.width, rect.height) * 0.5f - Feather;
        if (limit < 2f || Mathf.Abs(sweepAngle) < 0.01f || fill <= 0.0001f)
        {
            return;
        }

        float outer = limit;
        float usedThickness = Mathf.Min(thickness, outer * 0.92f);
        float inner = Mathf.Max(Feather, outer - usedThickness);
        float drawn = sweepAngle * fill;
        int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(drawn) / 3.5f), 1, 96);
        Vector2 origin = rect.center;
        Color32 solid = color;
        Color32 clear = solid;
        clear.a = 0;

        AddRadialStrip(vh, origin, startAngle, drawn, inner - Feather, inner, clear, solid, steps);
        AddRadialStrip(vh, origin, startAngle, drawn, inner, outer, solid, solid, steps);
        AddRadialStrip(vh, origin, startAngle, drawn, outer, outer + Feather, solid, clear, steps);

        if (!roundedCaps)
        {
            return;
        }

        float mid = (inner + outer) * 0.5f;
        float capRadius = (outer - inner) * 0.5f;
        AddCap(vh, origin, startAngle, mid, capRadius, -Mathf.Sign(sweepAngle), solid, clear);
        AddCap(vh, origin, startAngle + drawn, mid, capRadius, Mathf.Sign(sweepAngle), solid, clear);
    }

    public void Configure(float startDegrees, float sweepDegrees, float thicknessPixels, bool caps = true)
    {
        startAngle = startDegrees;
        sweepAngle = sweepDegrees;
        thickness = Mathf.Max(1f, thicknessPixels);
        roundedCaps = caps;
        SetVerticesDirty();
    }

    private static void AddRadialStrip(
        VertexHelper vh,
        Vector2 origin,
        float startDegrees,
        float sweepDegrees,
        float radius0,
        float radius1,
        Color32 color0,
        Color32 color1,
        int steps)
    {
        int start = vh.currentVertCount;
        float span = Mathf.Max(1, steps);
        for (int i = 0; i <= steps; i++)
        {
            float degrees = startDegrees + sweepDegrees * (i / span);
            float radians = degrees * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            vh.AddVert(origin + direction * radius0, color0, Vector2.zero);
            vh.AddVert(origin + direction * radius1, color1, Vector2.zero);
        }

        for (int i = 0; i < steps; i++)
        {
            int vert = start + i * 2;
            vh.AddTriangle(vert, vert + 2, vert + 1);
            vh.AddTriangle(vert + 1, vert + 2, vert + 3);
        }
    }

    private static void AddCap(
        VertexHelper vh,
        Vector2 origin,
        float degrees,
        float midRadius,
        float capRadius,
        float direction,
        Color32 solid,
        Color32 clear)
    {
        float radians = degrees * Mathf.Deg2Rad;
        Vector2 radial = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        Vector2 tangent = new Vector2(-radial.y, radial.x) * direction;
        Vector2 side = new Vector2(-tangent.y, tangent.x);
        Vector2 capCenter = origin + radial * midRadius;
        const int steps = 12;

        int center = vh.currentVertCount;
        vh.AddVert(capCenter, solid, Vector2.zero);
        for (int i = 0; i <= steps; i++)
        {
            float angle = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, i / (float)steps);
            Vector2 point = capCenter + tangent * (Mathf.Cos(angle) * capRadius) + side * (Mathf.Sin(angle) * capRadius);
            vh.AddVert(point, solid, Vector2.zero);
        }

        for (int i = 0; i < steps; i++)
        {
            vh.AddTriangle(center, center + 1 + i, center + 2 + i);
        }

        int ring = vh.currentVertCount;
        float outer = capRadius + Feather;
        for (int i = 0; i <= steps; i++)
        {
            float angle = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, i / (float)steps);
            Vector2 outward = tangent * Mathf.Cos(angle) + side * Mathf.Sin(angle);
            vh.AddVert(capCenter + outward * capRadius, solid, Vector2.zero);
            vh.AddVert(capCenter + outward * outer, clear, Vector2.zero);
        }

        for (int i = 0; i < steps; i++)
        {
            int vert = ring + i * 2;
            vh.AddTriangle(vert, vert + 2, vert + 1);
            vh.AddTriangle(vert + 1, vert + 2, vert + 3);
        }
    }
}
