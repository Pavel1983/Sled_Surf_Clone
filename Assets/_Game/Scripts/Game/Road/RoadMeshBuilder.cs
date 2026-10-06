using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns arrays of road vertices into Unity meshes. Pure geometry: it keeps no state and knows nothing
/// about the spline, the snow or the scene.
/// </summary>
public static class RoadMeshBuilder
{
    // A sculpted lip can rise more than a hundred meters between two samples.
    // One triangle that long is a few pixels wide and a hundred tall, and the GPU
    // shades its whole bounding box. Edges longer than this are split.
    private const float MaxVisualEdgeMeters = 8f;

    public static void AccumulateNormals(Vector3[] vertices, int[] triangles, Vector3[] normals)
    {
        Array.Clear(normals, 0, normals.Length);
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];
            Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            normals[a] += normal;
            normals[b] += normal;
            normals[c] += normal;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            if (normals[i].sqrMagnitude > 1e-10f)
            {
                normals[i].Normalize();
            }
            else
            {
                normals[i] = Vector3.up;
            }
        }
    }

    public static void UploadMesh(ref Mesh mesh, string meshName, Vector3[] vertices, Vector2[] uvs, Vector2[] parametric, int[] triangles, bool padBounds)
    {
        if (mesh == null)
        {
            mesh = new Mesh { name = meshName };
            mesh.hideFlags = HideFlags.HideAndDontSave;
        }

        mesh.Clear();
        mesh.indexFormat = vertices.Length > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.uv2 = parametric;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (padBounds)
        {
            // The shader lifts snow off this mesh. Padding keeps the drifts inside the bounds.
            var bounds = mesh.bounds;
            bounds.Expand(3f);
            mesh.bounds = bounds;
        }
    }

    public static void FillVisualChunk(
        Mesh mesh,
        Vector3[] vertices,
        Vector2[] uvs,
        Vector2[] parametric,
        Vector3[] normals,
        int across,
        int segmentStart,
        int segmentCount,
        int colStart,
        int bandQuads,
        bool flipped)
    {
        int localAcross = bandQuads + 1;
        int localRings = segmentCount + 1;
        var chunkVertices = new Vector3[localRings * localAcross];
        var chunkUvs = new Vector2[chunkVertices.Length];
        var chunkParametric = new Vector2[chunkVertices.Length];
        var chunkNormals = new Vector3[chunkVertices.Length];
        for (int ring = 0; ring < localRings; ring++)
        {
            int source = (segmentStart + ring) * across + colStart;
            int dest = ring * localAcross;
            Array.Copy(vertices, source, chunkVertices, dest, localAcross);
            Array.Copy(uvs, source, chunkUvs, dest, localAcross);
            Array.Copy(parametric, source, chunkParametric, dest, localAcross);
            Array.Copy(normals, source, chunkNormals, dest, localAcross);
        }

        if (!ChunkHasLongEdge(chunkVertices, localAcross, localRings))
        {
            var chunkTriangles = new int[segmentCount * bandQuads * 6];
            int cursor = 0;
            for (int ring = 0; ring < segmentCount; ring++)
            {
                for (int column = 0; column < bandQuads; column++)
                {
                    int bottomLeft = ring * localAcross + column;
                    int bottomRight = bottomLeft + 1;
                    int topLeft = bottomLeft + localAcross;
                    int topRight = topLeft + 1;
                    WriteQuad(chunkTriangles, ref cursor, bottomLeft, bottomRight, topLeft, topRight, flipped);
                }
            }

            UploadChunkMesh(mesh, chunkVertices, chunkUvs, chunkParametric, chunkNormals, chunkTriangles);
            return;
        }

        var splitVerts = new List<Vector3>(chunkVertices.Length * 2);
        var splitUvs = new List<Vector2>(chunkVertices.Length * 2);
        var splitParametric = new List<Vector2>(chunkVertices.Length * 2);
        var splitNormals = new List<Vector3>(chunkVertices.Length * 2);
        var splitTris = new List<int>(segmentCount * bandQuads * 12);
        for (int ring = 0; ring < segmentCount; ring++)
        {
            for (int column = 0; column < bandQuads; column++)
            {
                int bottomLeft = ring * localAcross + column;
                int bottomRight = bottomLeft + 1;
                int topLeft = bottomLeft + localAcross;
                int topRight = topLeft + 1;
                AppendSplitQuad(
                    splitVerts,
                    splitUvs,
                    splitParametric,
                    splitNormals,
                    splitTris,
                    chunkVertices,
                    chunkUvs,
                    chunkParametric,
                    chunkNormals,
                    bottomLeft,
                    bottomRight,
                    topLeft,
                    topRight,
                    flipped);
            }
        }

        UploadChunkMesh(
            mesh,
            splitVerts.ToArray(),
            splitUvs.ToArray(),
            splitParametric.ToArray(),
            splitNormals.ToArray(),
            splitTris.ToArray());
    }

    private static bool ChunkHasLongEdge(Vector3[] vertices, int localAcross, int rings)
    {
        float limit = MaxVisualEdgeMeters * MaxVisualEdgeMeters;
        for (int ring = 0; ring < rings; ring++)
        {
            int row = ring * localAcross;
            for (int column = 0; column < localAcross; column++)
            {
                if (column + 1 < localAcross
                    && (vertices[row + column] - vertices[row + column + 1]).sqrMagnitude > limit)
                {
                    return true;
                }

                if (ring + 1 < rings
                    && (vertices[row + column] - vertices[row + localAcross + column]).sqrMagnitude > limit)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void AppendSplitQuad(
        List<Vector3> positions,
        List<Vector2> uvs,
        List<Vector2> parametric,
        List<Vector3> normals,
        List<int> triangles,
        Vector3[] sourcePositions,
        Vector2[] sourceUvs,
        Vector2[] sourceParametric,
        Vector3[] sourceNormals,
        int bottomLeft,
        int bottomRight,
        int topLeft,
        int topRight,
        bool flipped)
    {
        // Points on a shared edge depend only on that edge, so the quad on the other
        // side cuts it in the same places and the seam does not open.
        int blIndex = AddSourceVert(positions, uvs, parametric, normals, sourcePositions, sourceUvs, sourceParametric, sourceNormals, bottomLeft);
        int brIndex = AddSourceVert(positions, uvs, parametric, normals, sourcePositions, sourceUvs, sourceParametric, sourceNormals, bottomRight);
        int tlIndex = AddSourceVert(positions, uvs, parametric, normals, sourcePositions, sourceUvs, sourceParametric, sourceNormals, topLeft);
        int trIndex = AddSourceVert(positions, uvs, parametric, normals, sourcePositions, sourceUvs, sourceParametric, sourceNormals, topRight);

        var loop = new List<int>(16);
        if (!flipped)
        {
            AddEdgePoints(loop, positions, uvs, parametric, normals, blIndex, tlIndex);
            AddEdgePoints(loop, positions, uvs, parametric, normals, tlIndex, trIndex);
            AddEdgePoints(loop, positions, uvs, parametric, normals, trIndex, brIndex);
            AddEdgePoints(loop, positions, uvs, parametric, normals, brIndex, blIndex);
        }
        else
        {
            AddEdgePoints(loop, positions, uvs, parametric, normals, blIndex, brIndex);
            AddEdgePoints(loop, positions, uvs, parametric, normals, brIndex, trIndex);
            AddEdgePoints(loop, positions, uvs, parametric, normals, trIndex, tlIndex);
            AddEdgePoints(loop, positions, uvs, parametric, normals, tlIndex, blIndex);
        }

        int center = AddLerpVert(positions, uvs, parametric, normals, blIndex, trIndex, 0.5f);
        for (int i = 0; i < loop.Count; i++)
        {
            int start = loop[i];
            int end = loop[(i + 1) % loop.Count];
            SplitLongTriangle(positions, uvs, parametric, normals, triangles, start, end, center, true, false, false, 8);
        }
    }

    private static void AddEdgePoints(
        List<int> loop,
        List<Vector3> positions,
        List<Vector2> uvs,
        List<Vector2> parametric,
        List<Vector3> normals,
        int from,
        int to)
    {
        int steps = EdgeSteps(positions[from], positions[to]);
        loop.Add(from);
        for (int step = 1; step < steps; step++)
        {
            loop.Add(AddLerpVert(positions, uvs, parametric, normals, from, to, step / (float)steps));
        }
    }

    private static int EdgeSteps(Vector3 a, Vector3 b)
    {
        float span = Vector3.Distance(a, b);
        int steps = Mathf.CeilToInt(span / MaxVisualEdgeMeters);
        if (steps < 1)
        {
            return 1;
        }
        // A 180 m lip is about twenty steps. Cap a broken edge so one quad cannot explode.
        return Mathf.Min(steps, 32);
    }

    private static int AddSourceVert(
        List<Vector3> positions,
        List<Vector2> uvs,
        List<Vector2> parametric,
        List<Vector3> normals,
        Vector3[] sourcePositions,
        Vector2[] sourceUvs,
        Vector2[] sourceParametric,
        Vector3[] sourceNormals,
        int source)
    {
        positions.Add(sourcePositions[source]);
        uvs.Add(sourceUvs[source]);
        parametric.Add(sourceParametric[source]);
        normals.Add(sourceNormals[source]);
        return positions.Count - 1;
    }

    private static int AddLerpVert(
        List<Vector3> positions,
        List<Vector2> uvs,
        List<Vector2> parametric,
        List<Vector3> normals,
        int from,
        int to,
        float t)
    {
        positions.Add(Vector3.LerpUnclamped(positions[from], positions[to], t));
        uvs.Add(Vector2.LerpUnclamped(uvs[from], uvs[to], t));
        parametric.Add(Vector2.LerpUnclamped(parametric[from], parametric[to], t));
        Vector3 normal = Vector3.LerpUnclamped(normals[from], normals[to], t);
        normals.Add(normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up);
        return positions.Count - 1;
    }

    private static void SplitLongTriangle(
        List<Vector3> positions,
        List<Vector2> uvs,
        List<Vector2> parametric,
        List<Vector3> normals,
        List<int> triangles,
        int a,
        int b,
        int c,
        bool boundaryAB,
        bool boundaryBC,
        bool boundaryCA,
        int depth)
    {
        float limit = MaxVisualEdgeMeters * MaxVisualEdgeMeters;
        float ab = (positions[a] - positions[b]).sqrMagnitude;
        float bc = (positions[b] - positions[c]).sqrMagnitude;
        float ca = (positions[c] - positions[a]).sqrMagnitude;
        bool longAB = !boundaryAB && ab > limit;
        bool longBC = !boundaryBC && bc > limit;
        bool longCA = !boundaryCA && ca > limit;
        if (depth <= 0 || (!longAB && !longBC && !longCA))
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
            return;
        }

        if (longAB && ab >= bc && ab >= ca)
        {
            int mid = AddLerpVert(positions, uvs, parametric, normals, a, b, 0.5f);
            SplitLongTriangle(positions, uvs, parametric, normals, triangles, a, mid, c, false, false, boundaryCA, depth - 1);
            SplitLongTriangle(positions, uvs, parametric, normals, triangles, mid, b, c, false, boundaryBC, false, depth - 1);
            return;
        }

        if (longBC && bc >= ca)
        {
            int mid = AddLerpVert(positions, uvs, parametric, normals, b, c, 0.5f);
            SplitLongTriangle(positions, uvs, parametric, normals, triangles, a, b, mid, boundaryAB, false, false, depth - 1);
            SplitLongTriangle(positions, uvs, parametric, normals, triangles, a, mid, c, false, false, boundaryCA, depth - 1);
            return;
        }

        int split = AddLerpVert(positions, uvs, parametric, normals, c, a, 0.5f);
        SplitLongTriangle(positions, uvs, parametric, normals, triangles, a, b, split, boundaryAB, false, false, depth - 1);
        SplitLongTriangle(positions, uvs, parametric, normals, triangles, split, b, c, false, boundaryBC, false, depth - 1);
    }

    private static void WriteQuad(int[] triangles, ref int cursor, int bottomLeft, int bottomRight, int topLeft, int topRight, bool flipped)
    {
        if (!flipped)
        {
            triangles[cursor++] = bottomLeft;
            triangles[cursor++] = topLeft;
            triangles[cursor++] = bottomRight;
            triangles[cursor++] = bottomRight;
            triangles[cursor++] = topLeft;
            triangles[cursor++] = topRight;
            return;
        }

        triangles[cursor++] = bottomLeft;
        triangles[cursor++] = bottomRight;
        triangles[cursor++] = topLeft;
        triangles[cursor++] = bottomRight;
        triangles[cursor++] = topRight;
        triangles[cursor++] = topLeft;
    }

    private static void UploadChunkMesh(Mesh mesh, Vector3[] vertices, Vector2[] uvs, Vector2[] parametric, Vector3[] normals, int[] triangles)
    {
        mesh.Clear();
        mesh.indexFormat = vertices.Length > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.uv2 = parametric;
        mesh.normals = normals;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        var bounds = mesh.bounds;
        bounds.Expand(3f);
        mesh.bounds = bounds;
    }
}
