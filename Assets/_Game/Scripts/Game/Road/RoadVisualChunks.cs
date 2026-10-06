using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The drawn surface of a road: the fine visual ribbon, cut into chunk objects under one hidden root.
/// It owns those objects, their meshes, and the private copy of the road material they draw with.
/// </summary>
public class RoadVisualChunks
{
    // Short pieces so the camera draws the nearby road, not the whole ribbon.
    private const int ChunkSegments = 32;

    // The road is hundreds of meters wide. One mesh that wide stays in the frustum
    // even when only the center is on screen, so each length piece is split into bands.
    private const int BandCount = 4;

    private readonly Transform parent;
    private readonly List<Mesh> visualChunkMeshes = new List<Mesh>();
    private readonly List<MeshRenderer> visualChunkRenderers = new List<MeshRenderer>();
    private readonly List<Material> chunkDebugMaterials = new List<Material>();
    private Transform visualChunkRoot;
    private Material runtimeRoadMaterial;
    private Material runtimeRoadSource;
    private bool debugChunksVisible;

    /// <summary>The private copy of the road material. Null until the road has a material.</summary>
    public Material Material => runtimeRoadMaterial;

    /// <summary>True while every chunk is painted its own flat color.</summary>
    public bool DebugVisible => debugChunksVisible;

    public RoadVisualChunks(Transform parent)
    {
        this.parent = parent;
    }

    /// <summary>
    /// Cuts the visual road into short bands so the camera draws the nearby center
    /// and leaves the side lips and the rest of the track outside the frustum.
    /// Normals are copied from the full ribbon, so a vertex shared by two chunks keeps one normal.
    /// </summary>
    /// <param name="roadMaterial">The road material. The chunks draw with a private copy of it.</param>
    /// <param name="vertices">Road-local positions for the whole visual ribbon. Laid out ring by ring from the start of the spline, <paramref name="across"/> vertices per ring.</param>
    /// <param name="uvs">Albedo texture coordinates, one per vertex, parallel to <paramref name="vertices"/>. x runs across the road, y runs along it.</param>
    /// <param name="parametric">Spline coordinates per vertex, written to the mesh as uv2 for the snow mask. x is 0 at the left edge and 1 at the right. y is 0 at the start of the spline and 1 at the end.</param>
    /// <param name="normals">Road-local normals accumulated over the whole ribbon, one per vertex. Shared edges match because the accumulation happened before the split.</param>
    /// <param name="triangles">Triangle indices into <paramref name="vertices"/> for the whole ribbon, six per quad.</param>
    /// <param name="segments">Quad count along the spline. The ribbon has <paramref name="segments"/> + 1 vertex rings.</param>
    /// <param name="across">Vertex count across the road, including both edges.</param>
    public void Build(Material roadMaterial, Vector3[] vertices, Vector2[] uvs, Vector2[] parametric, Vector3[] normals, int[] triangles, int segments, int across)
    {
        RefreshMaterial(roadMaterial);
        TryCreateVisualChunkRoot();

        int alongCount = Mathf.Max(1, Mathf.CeilToInt(segments / (float)ChunkSegments));
        // BuildSurface flips the whole ribbon together. The second index of the first
        // triangle is the far ring when the winding was left as written.
        bool flipped = triangles.Length >= 3 && triangles[1] != across;
        int chunkIndex = 0;
        for (int chunk = 0; chunk < alongCount; chunk++)
        {
            int segmentStart = chunk * ChunkSegments;
            int segmentCount = Mathf.Min(ChunkSegments, segments - segmentStart);
            int colStart = 0;
            int quadsLeft = across - 1;
            int bands = Mathf.Max(1, BandCount);
            for (int band = 0; band < bands && quadsLeft > 0; band++)
            {
                int bandsLeft = bands - band;
                int bandQuads = Mathf.Max(1, Mathf.CeilToInt(quadsLeft / (float)bandsLeft));
                if (bandQuads > quadsLeft)
                {
                    bandQuads = quadsLeft;
                }

                MeshRenderer chunkRenderer = GetVisualChunk(chunkIndex);
                RoadMeshBuilder.FillVisualChunk(
                    visualChunkMeshes[chunkIndex],
                    vertices,
                    uvs,
                    parametric,
                    normals,
                    across,
                    segmentStart,
                    segmentCount,
                    colStart,
                    bandQuads,
                    flipped);
                chunkRenderer.GetComponent<MeshFilter>().sharedMesh = visualChunkMeshes[chunkIndex];
                chunkRenderer.gameObject.SetActive(true);

                colStart += bandQuads;
                quadsLeft -= bandQuads;
                chunkIndex++;
            }
        }

        for (int chunk = chunkIndex; chunk < visualChunkRenderers.Count; chunk++)
        {
            if (visualChunkRenderers[chunk] != null)
            {
                visualChunkRenderers[chunk].gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Makes sure the chunks draw with a private copy of the road material.
    /// The copy carries the snow texture, so the asset itself is never written to.
    /// </summary>
    public void RefreshMaterial(Material roadMaterial)
    {
        if (roadMaterial == null)
        {
            return;
        }

        if (runtimeRoadMaterial != null && runtimeRoadSource == roadMaterial)
        {
            return;
        }

        ReleaseRuntimeRoadMaterial();
        runtimeRoadMaterial = new Material(roadMaterial)
        {
            name = "RoadSnowRuntime",
            hideFlags = HideFlags.HideAndDontSave
        };
        runtimeRoadSource = roadMaterial;
        for (int i = 0; i < visualChunkRenderers.Count; i++)
        {
            if (visualChunkRenderers[i] != null)
            {
                visualChunkRenderers[i].sharedMaterial = runtimeRoadMaterial;
            }
        }
    }

    // Solid colors instead of the snow material, so each chunk reads as its own mesh.
    public void SetDebugVisible(bool visible)
    {
        debugChunksVisible = visible;
        if (!visible)
        {
            for (int i = 0; i < visualChunkRenderers.Count; i++)
            {
                if (visualChunkRenderers[i] != null && runtimeRoadMaterial != null)
                {
                    visualChunkRenderers[i].sharedMaterial = runtimeRoadMaterial;
                }
            }

            ReleaseChunkDebugMaterials();
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            return;
        }

        for (int i = 0; i < visualChunkRenderers.Count; i++)
        {
            MeshRenderer chunkRenderer = visualChunkRenderers[i];
            if (chunkRenderer == null)
            {
                continue;
            }

            while (chunkDebugMaterials.Count <= i)
            {
                chunkDebugMaterials.Add(null);
            }

            if (chunkDebugMaterials[i] == null)
            {
                Color color = Color.HSVToRGB(Mathf.Repeat(i * 0.17f, 1f), 0.65f, 1f);
                var material = new Material(shader)
                {
                    name = "RoadChunkDebug",
                    hideFlags = HideFlags.HideAndDontSave,
                    color = color
                };
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", color);
                }

                chunkDebugMaterials[i] = material;
            }

            chunkRenderer.sharedMaterial = chunkDebugMaterials[i];
        }
    }

    /// <summary>Destroys the chunk objects, their meshes and the private materials.</summary>
    public void Release()
    {
        ReleaseChunkDebugMaterials();
        ReleaseVisualChunks();
        ReleaseRuntimeRoadMaterial();
    }

    private bool TryCreateVisualChunkRoot()
    {
        if (visualChunkRoot != null)
        {
            return false;
        }

        var rootObject = new GameObject("RoadSnowChunks");
        rootObject.hideFlags = HideFlags.HideAndDontSave;
        rootObject.transform.SetParent(parent, false);
        visualChunkRoot = rootObject.transform;
        return true;
    }

    private MeshRenderer GetVisualChunk(int index)
    {
        while (visualChunkRenderers.Count <= index)
        {
            var chunkObject = new GameObject("RoadSnowChunk");
            chunkObject.hideFlags = HideFlags.HideAndDontSave;
            chunkObject.transform.SetParent(visualChunkRoot, false);
            chunkObject.AddComponent<MeshFilter>();
            var renderer = chunkObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            if (runtimeRoadMaterial != null)
            {
                renderer.sharedMaterial = runtimeRoadMaterial;
            }

            var mesh = new Mesh { name = "RoadSnowChunk", hideFlags = HideFlags.HideAndDontSave };
            visualChunkMeshes.Add(mesh);
            visualChunkRenderers.Add(renderer);
        }

        var chunkRenderer = visualChunkRenderers[index];
        if (runtimeRoadMaterial != null && chunkRenderer.sharedMaterial != runtimeRoadMaterial)
        {
            chunkRenderer.sharedMaterial = runtimeRoadMaterial;
        }

        return chunkRenderer;
    }

    private void ReleaseRuntimeRoadMaterial()
    {
        if (runtimeRoadMaterial == null)
        {
            return;
        }

        Release(runtimeRoadMaterial);

        runtimeRoadMaterial = null;
        runtimeRoadSource = null;
    }

    private void ReleaseChunkDebugMaterials()
    {
        for (int i = 0; i < chunkDebugMaterials.Count; i++)
        {
            if (chunkDebugMaterials[i] == null)
            {
                continue;
            }

            Release(chunkDebugMaterials[i]);
        }

        chunkDebugMaterials.Clear();
    }

    private void ReleaseVisualChunks()
    {
        for (int i = 0; i < visualChunkMeshes.Count; i++)
        {
            if (visualChunkMeshes[i] == null)
            {
                continue;
            }

            Release(visualChunkMeshes[i]);
        }

        visualChunkMeshes.Clear();
        visualChunkRenderers.Clear();
        debugChunksVisible = false;
        if (visualChunkRoot == null)
        {
            return;
        }

        Release(visualChunkRoot.gameObject);

        visualChunkRoot = null;
    }

    private static void Release(Object target)
    {
        if (Application.isPlaying)
        {
            Object.Destroy(target);
        }
        else
        {
            Object.DestroyImmediate(target);
        }
    }
}
