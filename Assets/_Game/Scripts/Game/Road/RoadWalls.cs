using UnityEngine;

/// <summary>
/// The two side walls of a road. They are invisible fences with zero friction:
/// they only keep the sled on the ribbon. Each wall is a child object with its own mesh and collider.
/// </summary>
public class RoadWalls
{
    private const string LeftName = "RoadWallLeft";
    private const string RightName = "RoadWallRight";

    private readonly Transform parent;
    private Mesh leftMesh;
    private Mesh rightMesh;
    private Transform left;
    private Transform right;
    private PhysicsMaterial wallPhysics;
    private Material runtimeWallMaterial;

    public RoadWalls(Transform parent)
    {
        this.parent = parent;
    }

    /// <summary>
    /// Extrudes both walls from the edges of the collider ribbon. A height of zero turns them off.
    /// </summary>
    /// <param name="roadVertices">Collider vertices, ring by ring, <paramref name="across"/> per ring.</param>
    /// <param name="ups">Spline up at each ring. The walls rise along it.</param>
    public void Build(float wallHeight, Vector3[] roadVertices, Vector3[] ups, int across, int segments)
    {
        BuildWall(ref left, ref leftMesh, LeftName, wallHeight, roadVertices, ups, 0, across, segments);
        BuildWall(ref right, ref rightMesh, RightName, wallHeight, roadVertices, ups, across - 1, across, segments);
    }

    private void BuildWall(ref Transform wall, ref Mesh mesh, string wallName, float wallHeight, Vector3[] roadVertices, Vector3[] ups, int edge, int across, int segments)
    {
        wall = FindOrCreateWall(wall, wallName);
        bool active = wallHeight > 0.001f;
        wall.gameObject.SetActive(active);
        if (!active)
        {
            return;
        }

        int rings = segments + 1;
        var vertices = new Vector3[rings * 2];
        var triangles = new int[segments * 6];
        float height = wallHeight;

        for (int i = 0; i < rings; i++)
        {
            Vector3 bottom = roadVertices[i * across + edge];
            vertices[i * 2] = bottom;
            vertices[i * 2 + 1] = bottom + ups[i] * height;
        }

        bool leftEdge = edge == 0;
        for (int i = 0; i < segments; i++)
        {
            int bottom0 = i * 2;
            int top0 = bottom0 + 1;
            int bottom1 = bottom0 + 2;
            int top1 = bottom0 + 3;
            int tri = i * 6;
            if (leftEdge)
            {
                triangles[tri] = bottom0;
                triangles[tri + 1] = top0;
                triangles[tri + 2] = bottom1;
                triangles[tri + 3] = bottom1;
                triangles[tri + 4] = top0;
                triangles[tri + 5] = top1;
            }
            else
            {
                triangles[tri] = bottom0;
                triangles[tri + 1] = bottom1;
                triangles[tri + 2] = top0;
                triangles[tri + 3] = bottom1;
                triangles[tri + 4] = top1;
                triangles[tri + 5] = top0;
            }
        }

        if (mesh == null)
        {
            mesh = new Mesh { name = wallName };
            mesh.hideFlags = HideFlags.HideAndDontSave;
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var filter = wall.GetComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        var collider = wall.GetComponent<MeshCollider>();
        collider.convex = false;
        collider.sharedMesh = null;
        collider.sharedMesh = mesh;
        collider.sharedMaterial = WallPhysics();

        var renderer = wall.GetComponent<MeshRenderer>();
        Material surface = WallSurfaceMaterial();
        if (surface != null)
        {
            renderer.sharedMaterial = surface;
        }
        // The wall shader is unlit, and a 4 km ribbon in four shadow cascades is pure overhead.
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private Transform FindOrCreateWall(Transform cached, string wallName)
    {
        if (cached != null)
        {
            return cached;
        }

        Transform found = parent.Find(wallName);
        if (found == null)
        {
            var wallObject = new GameObject(wallName);
            wallObject.transform.SetParent(parent, false);
            found = wallObject.transform;
        }

        if (found.GetComponent<MeshFilter>() == null)
        {
            found.gameObject.AddComponent<MeshFilter>();
        }

        if (found.GetComponent<MeshCollider>() == null)
        {
            found.gameObject.AddComponent<MeshCollider>();
        }

        if (found.GetComponent<MeshRenderer>() == null)
        {
            found.gameObject.AddComponent<MeshRenderer>();
        }

        return found;
    }

    private PhysicsMaterial WallPhysics()
    {
        if (wallPhysics == null)
        {
            wallPhysics = new PhysicsMaterial("RoadWall")
            {
                hideFlags = HideFlags.HideAndDontSave,
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum,
                frictionCombine = PhysicsMaterialCombine.Minimum,
            };
        }

        return wallPhysics;
    }

    private Material WallSurfaceMaterial()
    {
        if (runtimeWallMaterial != null)
        {
            return runtimeWallMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            return null;
        }

        runtimeWallMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, name = "RoadWall" };
        var color = new Color(0.9f, 0.95f, 1f, 1f);
        runtimeWallMaterial.SetColor("_BaseColor", color);
        runtimeWallMaterial.SetColor("_Color", color);
        return runtimeWallMaterial;
    }
}
