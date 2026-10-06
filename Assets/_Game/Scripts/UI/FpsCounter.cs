using UnityEngine;

/// <summary>
/// Draws a smoothed frames-per-second readout in the corner of the game view.
/// </summary>
public class FpsCounter : MonoBehaviour
{
    [SerializeField] private int fontSize = 28;
    [SerializeField] private Vector2 margin = new Vector2(16f, 16f);
    [SerializeField] private float sampleDuration = 0.25f;

    private float accumulatedTime;
    private int accumulatedFrames;
    private float framesPerSecond;
    private GUIStyle labelStyle;
    private GUIStyle shadowStyle;

    private void Update()
    {
        accumulatedTime += Time.unscaledDeltaTime;
        accumulatedFrames++;
        if (accumulatedTime < sampleDuration)
        {
            return;
        }

        framesPerSecond = accumulatedFrames / accumulatedTime;
        accumulatedTime = 0f;
        accumulatedFrames = 0;
    }

    private void OnGUI()
    {
        RefreshStyles();
        string text = $"{Mathf.RoundToInt(framesPerSecond)} FPS";
        var rect = new Rect(margin.x, margin.y, 400f, fontSize + 12f);
        var shadow = rect;
        shadow.x += 1f;
        shadow.y += 1f;
        GUI.Label(shadow, text, shadowStyle);
        GUI.Label(rect, text, labelStyle);
    }

    private void RefreshStyles()
    {
        if (labelStyle != null && labelStyle.fontSize == fontSize)
        {
            return;
        }

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold
        };
        labelStyle.normal.textColor = Color.white;

        shadowStyle = new GUIStyle(labelStyle);
        shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
    }
}
