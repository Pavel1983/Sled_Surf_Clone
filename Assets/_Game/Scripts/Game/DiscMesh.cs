using UnityEngine;

/// <summary>
/// Builds a short cylinder around the Y axis: the shape a coin is made of.
/// </summary>
public static class DiscMesh
{
    public static Mesh Create(float radius, float thickness, int segments)
    {
        var mesh = new Mesh { name = "Coin" };
        mesh.hideFlags = HideFlags.HideAndDontSave;
        segments = Mathf.Max(3, segments);
        int vertexCount = 2 + segments * 2;
        var vertices = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        float half = thickness * 0.5f;
        vertices[0] = new Vector3(0f, -half, 0f);
        vertices[1] = new Vector3(0f, half, 0f);
        normals[0] = Vector3.down;
        normals[1] = Vector3.up;

        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            int bottom = 2 + i;
            int top = 2 + segments + i;
            vertices[bottom] = new Vector3(x, -half, z);
            vertices[top] = new Vector3(x, half, z);
            var side = new Vector3(x, 0f, z).normalized;
            normals[bottom] = (Vector3.down + side).normalized;
            normals[top] = (Vector3.up + side).normalized;
        }

        var triangles = new int[segments * 12];
        int cursor = 0;
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int bottom = 2 + i;
            int bottomNext = 2 + next;
            int top = 2 + segments + i;
            int topNext = 2 + segments + next;

            triangles[cursor++] = 1;
            triangles[cursor++] = top;
            triangles[cursor++] = topNext;

            triangles[cursor++] = 0;
            triangles[cursor++] = bottomNext;
            triangles[cursor++] = bottom;

            triangles[cursor++] = bottom;
            triangles[cursor++] = top;
            triangles[cursor++] = topNext;

            triangles[cursor++] = bottom;
            triangles[cursor++] = topNext;
            triangles[cursor++] = bottomNext;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }
}
