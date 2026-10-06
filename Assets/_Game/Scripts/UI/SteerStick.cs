using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

/// <summary>
/// Floating stick that appears where the player presses during a run.
/// Horizontal drag steers. The base stays on the press, the knob follows the finger.
/// The ring and the knob are objects of the scene. This class only moves them.
/// </summary>
[DisallowMultipleComponent]
public class SteerStick : MonoBehaviour
{
    [SerializeField] private SlingshotLaunch launch;
    [Tooltip("Ring radius in pixels at the reference screen height.")]
    [SerializeField] private float ringRadius = 180f;
    [Tooltip("Knob radius in pixels at the reference screen height.")]
    [SerializeField] private float knobRadius = 64f;
    [SerializeField] private float referenceHeight = 1920f;
    [SerializeField] private float deadZone = 0.08f;

    [SerializeField] private RectTransform root;
    [SerializeField] private RectTransform knob;

    private bool tracking;
    private int trackedTouchId = -1;
    private Vector2 originScreen;
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();

    private void Update()
    {
        if (!launch.CanSteer)
        {
            EndStroke();
            return;
        }

        if (!TryReadPointer(out bool began, out bool ended, out Vector2 screen))
        {
            if (tracking)
            {
                EndStroke();
            }

            return;
        }

        if (began)
        {
            if (PointerHitsUi(screen))
            {
                EndStroke();
                return;
            }

            tracking = true;
            originScreen = screen;
        }

        if (!tracking)
        {
            return;
        }

        float radius = Scaled(ringRadius);
        Vector2 delta = screen - originScreen;
        if (delta.sqrMagnitude > radius * radius && radius > 1f)
        {
            delta *= radius / delta.magnitude;
        }

        Show(originScreen, delta, radius);
        float steer = radius > 1f ? delta.x / radius : 0f;
        if (Mathf.Abs(steer) < deadZone)
        {
            steer = 0f;
        }

        launch.SteerInput = Mathf.Clamp(steer, -1f, 1f);

        if (ended)
        {
            EndStroke();
        }
    }

    private void EndStroke()
    {
        tracking = false;
        trackedTouchId = -1;
        root.gameObject.SetActive(false);
        launch.SteerInput = 0f;
    }

    private bool TryReadPointer(out bool began, out bool ended, out Vector2 screen)
    {
        began = false;
        ended = false;
        screen = originScreen;

        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null && ReadTouch(touchscreen, out began, out ended, out screen))
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

        began = !tracking && mouse.leftButton.wasPressedThisFrame;
        ended = mouse.leftButton.wasReleasedThisFrame;
        screen = mouse.position.ReadValue();
        return true;
    }

    private bool ReadTouch(Touchscreen touchscreen, out bool began, out bool ended, out Vector2 screen)
    {
        began = false;
        ended = false;
        screen = originScreen;
        TouchControl active = null;
        bool sawTouch = false;

        for (int i = 0; i < touchscreen.touches.Count; i++)
        {
            TouchControl touch = touchscreen.touches[i];
            bool live = touch.press.isPressed || touch.press.wasReleasedThisFrame;
            if (!live)
            {
                continue;
            }

            sawTouch = true;
            int touchId = touch.touchId.ReadValue();
            if (tracking && touchId == trackedTouchId)
            {
                active = touch;
                break;
            }

            if (!tracking && touch.press.wasPressedThisFrame)
            {
                active = touch;
            }
        }

        if (!sawTouch)
        {
            return false;
        }

        if (active == null)
        {
            return false;
        }

        began = !tracking && active.press.wasPressedThisFrame;
        ended = active.press.wasReleasedThisFrame;
        screen = active.position.ReadValue();
        if (began)
        {
            trackedTouchId = active.touchId.ReadValue();
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

    private void Show(Vector2 screen, Vector2 knobDelta, float radius)
    {
        root.anchoredPosition = screen;
        root.sizeDelta = new Vector2(radius * 2f, radius * 2f);
        float knobSize = Scaled(knobRadius) * 2f;
        knob.sizeDelta = new Vector2(knobSize, knobSize);
        knob.anchoredPosition = knobDelta;
        root.gameObject.SetActive(true);
    }

    private float Scaled(float pixels)
    {
        float height = Mathf.Max(1f, referenceHeight);
        return pixels * Screen.height / height;
    }
}
