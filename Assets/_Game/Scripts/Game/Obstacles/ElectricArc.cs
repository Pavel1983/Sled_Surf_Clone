using UnityEngine;

/// <summary>
/// Makes a set of lightning quads crackle. Several times a second each bolt picks another strip
/// of the lightning texture, flips, and sometimes blinks out for a moment.
/// </summary>
public class ElectricArc : MonoBehaviour
{
    // The texture holds this many bolts side by side.
    private const int BoltColumns = 4;

    private static readonly int BaseMapScaleOffset = Shader.PropertyToID("_BaseMap_ST");

    [SerializeField] private Renderer[] bolts;
    [Tooltip("How many times a second the bolts change shape.")]
    [SerializeField] private float flickerRate = 16f;
    [Tooltip("Chance, from 0 to 1, that a bolt is dark on a given flicker.")]
    [SerializeField] private float blinkChance = 0.15f;
    [Tooltip("How many times the texture repeats along a bolt.")]
    [SerializeField] private float lengthTiling = 1.4f;

    private MaterialPropertyBlock block;
    private float nextFlickerTime;

    private void Awake()
    {
        block = new MaterialPropertyBlock();
    }

    private void Update()
    {
        if (Time.time < nextFlickerTime)
        {
            return;
        }

        nextFlickerTime = Time.time + 1f / Mathf.Max(1f, flickerRate);
        for (int i = 0; i < bolts.Length; i++)
        {
            Flicker(bolts[i]);
        }
    }

    private void Flicker(Renderer bolt)
    {
        bolt.enabled = Random.value >= blinkChance;
        if (!bolt.enabled)
        {
            return;
        }

        // One column of the texture across the quad, a random stretch of it along the quad.
        float column = Random.Range(0, BoltColumns) / (float)BoltColumns;
        float flip = Random.value < 0.5f ? -1f : 1f;
        bolt.GetPropertyBlock(block);
        block.SetVector(BaseMapScaleOffset, new Vector4(1f / BoltColumns, lengthTiling * flip, column, Random.value));
        bolt.SetPropertyBlock(block);
    }
}
