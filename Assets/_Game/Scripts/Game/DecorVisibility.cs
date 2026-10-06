using UnityEngine;

/// <summary>
/// Turns on only the scenery near the sled. The run is several kilometres long,
/// and props far ahead or far behind are too small to see but still cost time to cull and draw.
/// Sits on the parent of the props and manages its direct children.
/// </summary>
[DisallowMultipleComponent]
public class DecorVisibility : MonoBehaviour
{
    [SerializeField] private SlingshotLaunch launch;
    [Tooltip("Props farther than this from the sled, in meters, are turned off.")]
    [SerializeField] private float visibleRange = 350f;

    private GameObject[] props;
    private Vector3[] positions;

    private void Awake()
    {
        int count = transform.childCount;
        props = new GameObject[count];
        positions = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Transform child = transform.GetChild(i);
            props[i] = child.gameObject;
            // Props never move, so their positions are read once.
            positions[i] = child.position;
        }
    }

    private void LateUpdate()
    {
        Vector3 sled = launch.Body.position;
        float limit = visibleRange * visibleRange;
        for (int i = 0; i < props.Length; i++)
        {
            bool visible = (positions[i] - sled).sqrMagnitude <= limit;
            if (props[i].activeSelf != visible)
            {
                props[i].SetActive(visible);
            }
        }
    }
}
