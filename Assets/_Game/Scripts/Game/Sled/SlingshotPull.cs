using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// The slingshot on the start pad. It reads a finger or mouse pull, draws the band and the shot line,
/// and reports the release. It does not move the sled: whoever listens to <see cref="Released"/> does.
/// Pull length sets the speed. The shot direction is the road tangent, shifted by at most 20 degrees.
/// </summary>
[DisallowMultipleComponent]
public class SlingshotPull : MonoBehaviour
{
    /// <summary>
    /// Raised when the band is let go. Carries the shot direction and the launch speed
    /// in meters per second, before any upgrade is applied.
    /// </summary>
    public event Action<Vector3, float> Released;

    [SerializeField] private SplineRoad road;
    [Tooltip("Camera that turns a drag on the screen into a pull in meters.")]
    [SerializeField] private Camera aimCamera;
    [SerializeField] private LineRenderer pullLine;
    [SerializeField] private LineRenderer shotLine;
    [Tooltip("Meters per second added for each meter of pull, before the angle power factor.")]
    [SerializeField] private float impulsePerMeter = 5f;
    [Tooltip("Pull length, in meters, that is full power straight down the road. A longer pull does not launch faster. An angled shot keeps less of this.")]
    [SerializeField] private float fullPullMeters = 3.6f;

    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();

    // Where the sled sits. The band and the shot line start here.
    private Vector3 anchor;
    private Vector2 pressPixels;
    private Vector2 aimScreen;
    private int aimTouchId = -1;
    private Vector3 pull;
    private int aimDegrees;
    private float aimPower = 1f;
    private bool aiming;

    public bool IsAiming => aiming;

    /// <summary>
    /// Launch power from 0 to 1. Full pull straight down the road is 1.
    /// Pull past the full length does not add power, and an angled shot scales it down.
    /// </summary>
    public float Tension01 => aiming ? CappedPullMeters / MaxPullMeters * aimPower : 0f;

    private float MaxPullMeters => Mathf.Max(0.5f, fullPullMeters);

    private float CappedPullMeters => Mathf.Min(pull.magnitude, MaxPullMeters);

    /// <summary>
    /// Reads the pointer for this frame and moves the band with it. Call it every frame the slingshot is armed.
    /// </summary>
    public void ReadPull(Vector3 sledAnchor)
    {
        anchor = sledAnchor;
        if (!TryReadAimPointer(out bool began, out bool ended, out Vector2 screen))
        {
            if (aiming)
            {
                ReleaseAim(aimScreen);
            }

            return;
        }

        aimScreen = screen;
        if (began && !PointerHitsUi(screen))
        {
            aiming = true;
            pressPixels = screen;
            pull = Vector3.zero;
            aimDegrees = 0;
            aimPower = 1f;
        }

        if (!aiming)
        {
            return;
        }

        if (ended)
        {
            ReleaseAim(screen);
        }
        else
        {
            UpdateAim(screen);
        }
    }

    /// <summary>Drops a pull in progress without launching.</summary>
    public void Cancel()
    {
        aiming = false;
        aimTouchId = -1;
        SetLines(false);
    }

    private void ReleaseAim(Vector2 screen)
    {
        UpdateAim(screen);
        aiming = false;
        aimTouchId = -1;
        SetLines(false);
        // A tap without a pull is not a shot.
        if (pull.magnitude > 0.2f)
        {
            Released?.Invoke(DirectionFromDegrees(aimDegrees), CappedPullMeters * impulsePerMeter * aimPower);
        }
    }

    private bool TryReadAimPointer(out bool began, out bool ended, out Vector2 screen)
    {
        began = false;
        ended = false;
        screen = aimScreen;

        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null && ReadAimTouch(touchscreen, out began, out ended, out screen))
        {
            return true;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return false;
        }

        if (!mouse.leftButton.isPressed && !mouse.leftButton.wasReleasedThisFrame)
        {
            return false;
        }

        began = !aiming && mouse.leftButton.wasPressedThisFrame;
        ended = mouse.leftButton.wasReleasedThisFrame;
        screen = mouse.position.ReadValue();
        return true;
    }

    private bool ReadAimTouch(Touchscreen touchscreen, out bool began, out bool ended, out Vector2 screen)
    {
        began = false;
        ended = false;
        screen = aimScreen;
        TouchControl active = null;

        for (int i = 0; i < touchscreen.touches.Count; i++)
        {
            TouchControl touch = touchscreen.touches[i];
            bool live = touch.press.isPressed || touch.press.wasReleasedThisFrame;
            if (!live)
            {
                continue;
            }

            int touchId = touch.touchId.ReadValue();
            if (aiming && touchId == aimTouchId)
            {
                active = touch;
                break;
            }

            if (!aiming && touch.press.wasPressedThisFrame)
            {
                active = touch;
            }
        }

        if (active == null)
        {
            return false;
        }

        began = !aiming && active.press.wasPressedThisFrame;
        ended = active.press.wasReleasedThisFrame;
        screen = active.position.ReadValue();
        if (began)
        {
            aimTouchId = active.touchId.ReadValue();
        }

        return true;
    }

    private bool PointerHitsUi(Vector2 screen)
    {
        EventSystem events = EventSystem.current;
        if (events == null)
        {
            return false;
        }

        uiHits.Clear();
        var pointer = new PointerEventData(events) { position = screen };
        events.RaycastAll(pointer, uiHits);
        return uiHits.Count > 0;
    }

    private void UpdateAim(Vector2 pixels)
    {
        Camera camera = aimCamera;
        if (!road.TryGetSurface(0f, 0.5f, out _, out Vector3 tangent, out Vector3 up))
        {
            return;
        }

        Vector2 delta = pixels - pressPixels;
        float depth = Vector3.Distance(camera.transform.position, anchor);
        float worldHeight = 2f * depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float metersPerPixel = worldHeight / Mathf.Max(1, Screen.height);

        Vector3 right = Vector3.ProjectOnPlane(camera.transform.right, up);
        Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, up);
        if (right.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f)
        {
            return;
        }

        // The pocket follows the cursor. Dragging down the screen pulls back toward the camera.
        pull = (right.normalized * delta.x + forward.normalized * delta.y) * metersPerPixel;
        if (pull.sqrMagnitude > MaxPullMeters * MaxPullMeters)
        {
            pull = pull.normalized * MaxPullMeters;
        }

        Vector3 launchRaw = -pull;
        Vector3 roadForward = Vector3.ProjectOnPlane(tangent, up);
        if (launchRaw.sqrMagnitude < 0.0001f || roadForward.sqrMagnitude < 0.0001f)
        {
            aimDegrees = 0;
            aimPower = 1f;
            SetLines(false);
            return;
        }

        float signed = Vector3.SignedAngle(roadForward.normalized, launchRaw.normalized, up.sqrMagnitude > 0.0001f ? up : Vector3.up);
        aimDegrees = SlingshotAim.Quantize(signed);
        aimPower = SlingshotAim.PowerFactor(aimDegrees);

        Vector3 shot = DirectionFromDegrees(aimDegrees);
        // Same cap and angle factor as the launch, so the line stops growing when the gauge does.
        float shotLength = CappedPullMeters * aimPower;
        pullLine.SetPosition(0, anchor);
        pullLine.SetPosition(1, anchor + pull);
        shotLine.SetPosition(0, anchor);
        shotLine.SetPosition(1, anchor + shot * shotLength);
        SetLines(true);
    }

    private Vector3 DirectionFromDegrees(int degrees)
    {
        if (!road.TryGetSurface(0f, 0.5f, out _, out Vector3 tangent, out Vector3 up))
        {
            return transform.forward;
        }

        Vector3 axis = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
        Vector3 roadForward = Vector3.ProjectOnPlane(tangent, axis);
        if (roadForward.sqrMagnitude < 0.0001f)
        {
            return transform.forward;
        }

        return Quaternion.AngleAxis(degrees, axis) * roadForward.normalized;
    }

    private void SetLines(bool visible)
    {
        if (pullLine != null)
        {
            pullLine.enabled = visible;
        }

        if (shotLine != null)
        {
            shotLine.enabled = visible;
        }
    }
}
