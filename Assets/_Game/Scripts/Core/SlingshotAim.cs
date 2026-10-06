using UnityEngine;

/// <summary>
/// Turns a raw aim angle into a 1-degree step inside ±20°, and the matching power.
/// 0° and ±1° keep full power. ±20° keeps 80%. Steps in between fall in a straight line.
/// </summary>
public static class SlingshotAim
{
    public const int FullPowerDegrees = 1;
    public const int MaxDegrees = 20;
    public const float EdgePower = 0.8f;

    public static int Quantize(float signedDegrees)
    {
        return Mathf.Clamp(Mathf.RoundToInt(signedDegrees), -MaxDegrees, MaxDegrees);
    }

    public static float PowerFactor(int degrees)
    {
        int steps = Mathf.Abs(degrees);
        if (steps <= FullPowerDegrees)
        {
            return 1f;
        }

        float span = MaxDegrees - FullPowerDegrees;
        return 1f - (1f - EdgePower) * (steps - FullPowerDegrees) / span;
    }
}
