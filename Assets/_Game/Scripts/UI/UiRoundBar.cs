using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Vertical capsule used by the distance progress track and its fill.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class UiRoundBar : MaskableGraphic
{
    protected override void OnEnable()
    {
        base.OnEnable();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = rectTransform.rect;
        if (rect.width < 1f || rect.height < 1f)
        {
            return;
        }

        Color32 vertexColor = color;
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
        float midX = rect.center.x;
        if (rect.height <= rect.width + 0.5f)
        {
            AddDisc(vh, rect.center, radius, vertexColor);
            return;
        }

        var bottom = new Vector2(midX, rect.yMin + radius);
        var top = new Vector2(midX, rect.yMax - radius);
        AddHalfDisc(vh, bottom, Vector2.down, radius, vertexColor);
        AddHalfDisc(vh, top, Vector2.up, radius, vertexColor);

        int start = vh.currentVertCount;
        vh.AddVert(new Vector3(midX - radius, bottom.y), vertexColor, Vector2.zero);
        vh.AddVert(new Vector3(midX + radius, bottom.y), vertexColor, Vector2.zero);
        vh.AddVert(new Vector3(midX + radius, top.y), vertexColor, Vector2.zero);
        vh.AddVert(new Vector3(midX - radius, top.y), vertexColor, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    private static void AddDisc(VertexHelper vh, Vector2 center, float radius, Color32 color)
    {
        const int steps = 20;
        int hub = vh.currentVertCount;
        vh.AddVert(center, color, Vector2.zero);
        for (int i = 0; i <= steps; i++)
        {
            float angle = Mathf.PI * 2f * i / steps;
            var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            vh.AddVert(point, color, Vector2.zero);
        }

        for (int i = 0; i < steps; i++)
        {
            vh.AddTriangle(hub, hub + 1 + i, hub + 2 + i);
        }
    }

    private static void AddHalfDisc(VertexHelper vh, Vector2 center, Vector2 bulge, float radius, Color32 color)
    {
        const int steps = 12;
        Vector2 side = new Vector2(-bulge.y, bulge.x);
        int hub = vh.currentVertCount;
        vh.AddVert(center, color, Vector2.zero);
        for (int i = 0; i <= steps; i++)
        {
            float angle = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, i / (float)steps);
            Vector2 point = center + bulge * (Mathf.Cos(angle) * radius) + side * (Mathf.Sin(angle) * radius);
            vh.AddVert(point, color, Vector2.zero);
        }

        for (int i = 0; i < steps; i++)
        {
            vh.AddTriangle(hub, hub + 1 + i, hub + 2 + i);
        }
    }
}
