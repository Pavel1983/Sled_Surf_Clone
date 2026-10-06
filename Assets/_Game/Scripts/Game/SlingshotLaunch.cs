using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Holds a non-rotating body at the start of a <see cref="SplineRoad"/> and launches it from a finger or mouse pull.
/// Pull length sets the speed. The shot direction is the road tangent, shifted by at most 20 degrees.
/// </summary>
public class SlingshotLaunch : MonoBehaviour
{
    /// <summary>
    /// Raised when a major obstacle ends the run.
    /// </summary>
    public event System.Action Crashed;

    /// <summary>
    /// Raised once when a run ends, however it ends: a crash, a stop, the finish or a restart mid-ride.
    /// </summary>
    public event System.Action RunFinished;

    /// <summary>
    /// Raised when a minor obstacle takes speed off the sled.
    /// </summary>
    public event System.Action Slowed;

    /// <summary>
    /// Raised when TAP TO PLAY arms the slingshot.
    /// </summary>
    public event System.Action Armed;

    /// <summary>
    /// Raised every time the run returns to the start, right after RunSerial changes.
    /// </summary>
    public event System.Action RunReset;

    [SerializeField] private SplineRoad road;
    [Tooltip("Meters per second added for each meter of pull, before the angle power factor.")]
    [SerializeField] private float impulsePerMeter = 5f;
    [Tooltip("Pull length, in meters, that is full power straight down the road. A longer pull does not launch faster. An angled shot keeps less of this.")]
    [SerializeField] private float fullPullMeters = 3.6f;
    [SerializeField] private Transform visual;
    [Tooltip("Camera that turns a drag on the screen into a pull in meters.")]
    [SerializeField] private Camera aimCamera;
    [SerializeField] private LineRenderer pullLine;
    [SerializeField] private LineRenderer shotLine;
    [SerializeField] private ParticleSystem snowSpray;

    [Tooltip("Backward speed along the road, in meters per second, that ends the run.")]
    [SerializeField] private float reverseSpeed = 0.3f;
    [Tooltip("The run ends once the sled has stayed inside this radius, in meters.")]
    [SerializeField] private float restRadius = 0.45f;
    [Tooltip("How long the sled must stay put before the run ends, in seconds.")]
    [SerializeField] private float restDelay = 0.7f;
    [Tooltip("Fraction of the spline that counts as the finish. The mesh stays the full length. 0.7 ends the run at 70 percent so the finish can be reached without riding the whole ribbon.")]
    [SerializeField] private float finishFraction = 0.7f;

    [Tooltip("Seconds between a crash and the results screen. The crash animation plays in that gap.")]
    [SerializeField] private float crashResultsDelay = 2f;

    [Header("Snow Spray")]
    [Tooltip("Flake size multiplier while the sled is barely moving.")]
    [SerializeField] private float spraySizeSlow = 0.5f;
    [Tooltip("Flake size multiplier at Spray Full Speed and above.")]
    [SerializeField] private float spraySizeFast = 2.5f;
    [Tooltip("Speed, in meters per second, where the flakes reach their largest size.")]
    [SerializeField] private float sprayFullSpeed = 28f;

    [Header("Steer")]
    [Tooltip("Sideways acceleration at full stick on the road, in g. The turn rate is this divided by the speed, so the sled turns sharply when slow and gently when fast.")]
    [SerializeField] private float groundLateralG = 1.2f;
    [Tooltip("Sideways acceleration at full stick in the air, in g. Lower than on the road, so a jump corrects less.")]
    [SerializeField] private float airLateralG = 0.4f;
    [Tooltip("Fastest the heading may turn, in degrees per second. It caps the turn at low speed, where the acceleration limit alone would spin the sled.")]
    [SerializeField] private float maxTurnRate = 120f;
    [Tooltip("Seconds the turn takes to catch up with the stick. Larger feels heavier: the turn builds up and fades out instead of snapping. 0 follows the stick at once.")]
    [SerializeField] private float steerSmoothTime = 0.25f;
    [Tooltip("Stick response curve. 1 is linear. Larger makes a small stick move gentler, while full stick still gives the full turn.")]
    [SerializeField] private float steerCurve = 1.6f;
    [Tooltip("Full width, in degrees, of the cone the sled may travel in, centered on the road direction. 60 keeps the heading within 30 degrees to either side. 0 turns the limit off.")]
    [Range(0f, 180f)]
    [SerializeField] private float headingCone = 60f;

    private Rigidbody body;

    // Velocity going into the physics step. A collision callback only sees the velocity after the impact.
    private Vector3 velocityBeforeStep;
    private CapsuleCollider capsule;
    private RunEconomy economy;

    // Radius of the capsule. The seat rides this far above the road.
    private float radius;
    private Vector3 bodyUp = Vector3.up;
    private Vector3 anchor;
    private Vector3 stableFacing = Vector3.forward;
    private Vector2 pressPixels;
    private Vector2 aimScreen;
    private int aimTouchId = -1;
    private Vector3 pull;
    private int aimDegrees;
    private float aimPower = 1f;
    private bool placed;
    private bool aiming;
    private bool launched;
    private bool pendingLaunch;
    private bool stopped;
    private bool movedForward;
    private Vector3 pendingVelocity;
    private Vector3 stopPosition;
    private Vector3 restAnchor;
    private float restTimer;
    private float distanceMeters;
    private float runMaxMeters;
    private float previousMaxMeters;
    private float surfaceSpeed;
    private Vector3 surfaceNormal = Vector3.up;
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private bool trailValid;
    private Vector3 lastTrailPoint;

    // Last normalized position on the road. Keeps the next spline query in a short window.
    private float roadHintT = -1f;
    private float groundedGraceUntil;
    private float sprayBudget;
    private bool roadTouched;
    private bool hadRoadContact;
    private Vector3 roadContactPoint;
    private Vector3 roadContactNormal = Vector3.up;
    private float resultsTime;
    private float steer;
    private float steerVelocity;

    public Rigidbody Body => body;
    public Vector3 StableFacing => stableFacing;
    public bool IsAiming => aiming;
    public bool IsRiding => launched || pendingLaunch;
    public bool CanSteer => launched && !stopped;
    public bool IsStopped => stopped;

    /// <summary>
    /// True once the results screen may appear. A crash holds it back so the fall can play out.
    /// </summary>
    public bool ResultsReady => stopped && Time.time >= resultsTime;

    /// <summary>
    /// True while the sled is riding and touched the road on the last physics step.
    /// </summary>
    public bool IsGrounded => launched && !stopped && hadRoadContact;

    /// <summary>
    /// True after TAP TO PLAY, until the run is reset. The slingshot ignores input before that.
    /// </summary>
    public bool PlayArmed { get; private set; }

    /// <summary>
    /// Increases every time the run returns to the start, so pickups can respawn.
    /// </summary>
    public int RunSerial { get; private set; }

    /// <summary>
    /// Horizontal stick deflection after the launch, from -1 (left) to 1 (right).
    /// </summary>
    public float SteerInput { get; set; }
    public float DistanceMeters => distanceMeters;
    public float RunDistanceMeters => runMaxMeters;
    public float PreviousBestMeters => previousMaxMeters;

    /// <summary>
    /// Speed along the road surface, in meters per second. The part pointing out of the surface is left out.
    /// </summary>
    public float SurfaceSpeed => surfaceSpeed;

    public float RoadLengthMeters => road != null ? Mathf.Max(road.RoadLength(), 0.01f) : 1f;

    /// <summary>
    /// Distance along the road that ends the run. The road mesh is longer than this.
    /// </summary>
    public float FinishMeters => RoadLengthMeters * Mathf.Clamp(finishFraction, 0.05f, 1f);

    /// <summary>
    /// Launch power from 0 to 1. Full pull straight down the road is 1.
    /// Pull past <see cref="fullPullMeters"/> does not add power, and an angled shot scales it down.
    /// </summary>
    public float Tension01
    {
        get
        {
            if (!aiming)
            {
                return 0f;
            }

            return (CappedPullMeters / MaxPullMeters) * aimPower;
        }
    }

    private float MaxPullMeters => Mathf.Max(0.5f, fullPullMeters);

    private float CappedPullMeters => Mathf.Min(pull.magnitude, MaxPullMeters);

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        radius = capsule.radius;
    }

    private void Start()
    {
        placed = TryPlace();
        UpdateSlideSurface(false);
    }

    private void Update()
    {
        if (!placed)
        {
            placed = TryPlace();
        }

        if (!placed || body == null)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
        {
            ResetToStart();
            return;
        }

        if (launched)
        {
            SampleRide();
            return;
        }

        if (!PlayArmed)
        {
            return;
        }

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

        if (!ended)
        {
            UpdateAim(screen);
        }

        if (ended)
        {
            ReleaseAim(screen);
        }
    }

    private void FixedUpdate()
    {
        // OnCollisionStay runs after this method, so this is the previous physics step.
        hadRoadContact = roadTouched;
        roadTouched = false;

        if (body == null)
        {
            return;
        }

        if (stopped)
        {
            Hold(stopPosition);
            UpdateSlideSurface(false);
            return;
        }

        if (launched)
        {
            TryFinishRide();
            if (!stopped)
            {
                ApplySteer();
                LimitHeading();
                AlignBody();
                velocityBeforeStep = body.linearVelocity;
            }

            UpdateSlideSurface(!stopped);
            return;
        }

        Hold(anchor);
        UpdateSlideSurface(false);
        if (!pendingLaunch)
        {
            return;
        }

        pendingLaunch = false;
        launched = true;
        movedForward = false;
        body.isKinematic = false;
        // Continuous Dynamic sweeps the whole road mesh. On the fast downhill that sweep
        // gets expensive all at once. Speculative is the cheaper mode that still stops tunneling.
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.WakeUp();
        body.position = anchor;
        body.linearVelocity = pendingVelocity;
        velocityBeforeStep = pendingVelocity;
        body.angularVelocity = Vector3.zero;
        restAnchor = anchor;
        restTimer = 0f;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!launched || stopped)
        {
            return;
        }

        Obstacle obstacle = collision.collider.GetComponentInParent<Obstacle>();
        if (obstacle != null)
        {
            obstacle.Hit(this);
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        if (collision.collider != road.SurfaceCollider || collision.contactCount <= 0)
        {
            return;
        }

        ContactPoint contact = collision.GetContact(0);
        roadTouched = true;
        roadContactPoint = contact.point;
        roadContactNormal = contact.normal.sqrMagnitude > 0.0001f ? contact.normal.normalized : Vector3.up;
    }

    /// <summary>
    /// Hands over the economy the sled reads its upgrades from. Called once by the composition root.
    /// </summary>
    public void Initialize(RunEconomy runEconomy)
    {
        economy = runEconomy;
    }

    public void Restart()
    {
        ResetToStart();
    }

    public void ArmPlay()
    {
        if (launched || pendingLaunch || stopped)
        {
            return;
        }

        PlayArmed = true;
        Armed?.Invoke();
    }

    /// <summary>
    /// Ends the run where the sled is. A major obstacle calls this.
    /// </summary>
    public void Crash()
    {
        if (!launched || stopped || body == null)
        {
            return;
        }

        FinishRun();
        resultsTime = Time.time + Mathf.Max(0f, crashResultsDelay);
        Crashed?.Invoke();
    }

    /// <summary>
    /// Keeps a fraction of the velocity the sled had before this physics step. 0.35 drops it to about a third.
    /// A minor obstacle calls this from a collision, where the impact has already bent the current velocity.
    /// </summary>
    public void SlowDown(float keepFraction)
    {
        if (!launched || stopped || body == null || body.isKinematic)
        {
            return;
        }

        float keep = Mathf.Clamp(keepFraction, 0.05f, 0.9f);
        body.linearVelocity = velocityBeforeStep * keep;
        Slowed?.Invoke();
    }

    private void ReleaseAim(Vector2 screen)
    {
        UpdateAim(screen);
        aiming = false;
        aimTouchId = -1;
        SetLines(false);
        if (pull.magnitude > 0.2f)
        {
            Vector3 direction = DirectionFromDegrees(aimDegrees);
            pendingVelocity = direction * (CappedPullMeters * impulsePerMeter * aimPower * economy.LaunchScale);
            pendingLaunch = true;
            economy.BeginRun();
            stableFacing = direction;
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

    private void TryFinishRide()
    {
        bool reversing = false;
        if (road != null && road.TryGetSurfaceCoord(body.position, roadHintT, out float t, out _, out Vector3 forward))
        {
            roadHintT = t;
            float along = Vector3.Dot(body.linearVelocity, forward);
            if (along > 0.2f)
            {
                movedForward = true;
            }

            reversing = movedForward && along < -Mathf.Max(0.05f, reverseSpeed);
            float traveled = Mathf.Max(0f, t * road.RoadLength());
            if (traveled > runMaxMeters)
            {
                runMaxMeters = traveled;
            }

            if (traveled >= FinishMeters)
            {
                FinishRun();
                return;
            }
        }

        if (reversing)
        {
            FinishRun();
            return;
        }

        // A full stop used to leave the run open: the flag only appeared after a backward slide.
        float radius = Mathf.Max(0.05f, restRadius);
        if ((body.position - restAnchor).sqrMagnitude > radius * radius)
        {
            restAnchor = body.position;
            restTimer = 0f;
            return;
        }

        restTimer += Time.fixedDeltaTime;
        if (restTimer >= Mathf.Max(0.05f, restDelay))
        {
            FinishRun();
        }
    }

    private void FinishRun()
    {
        stopPosition = body.position;
        stopped = true;
        resultsTime = Time.time;
        restTimer = 0f;
        SteerInput = 0f;
        steer = 0f;
        steerVelocity = 0f;
        Hold(stopPosition);
        RunFinished?.Invoke();
    }

    private void ApplySteer()
    {
        if (body.isKinematic)
        {
            return;
        }

        // The stick sets a target. The turn eases toward it, so a flick does not jerk the sled
        // and the carve fades out after the finger lifts.
        float target = Mathf.Clamp(SteerInput, -1f, 1f);
        target = Mathf.Sign(target) * Mathf.Pow(Mathf.Abs(target), Mathf.Max(1f, steerCurve));
        steer = steerSmoothTime > 0.001f
            ? Mathf.SmoothDamp(steer, target, ref steerVelocity, steerSmoothTime, Mathf.Infinity, Time.fixedDeltaTime)
            : target;

        float input = steer;
        if (Mathf.Abs(input) < 0.002f)
        {
            return;
        }

        bool grounded = false;
        Vector3 axis = Vector3.up;
        if (road != null && road.TryGetSurfaceCoord(body.position, roadHintT, out float t, out float u, out _))
        {
            roadHintT = t;
            grounded = IsOnRoad(t, u, out _, out Vector3 normal);
            if (grounded && normal.sqrMagnitude > 0.0001f)
            {
                axis = normal.normalized;
            }
        }

        Vector3 velocity = body.linearVelocity;
        Vector3 vertical = Vector3.Project(velocity, axis);
        Vector3 planar = velocity - vertical;
        float speed = planar.magnitude;
        // Below this there is nothing to carve. A sled does not spin while it is nearly stopped.
        if (speed < 2f)
        {
            return;
        }

        // The stick asks for a share of the sideways acceleration the sled can take. On a curve
        // that acceleration is speed times turn rate, so the same stick turns less the faster it goes.
        float lateralAcceleration = input * (grounded ? groundLateralG : airLateralG) * Physics.gravity.magnitude;
        float turnRate = lateralAcceleration / speed * Mathf.Rad2Deg;
        turnRate = Mathf.Clamp(turnRate, -maxTurnRate, maxTurnRate);
        Vector3 turned = Quaternion.AngleAxis(turnRate * Time.fixedDeltaTime, axis) * planar;

        body.linearVelocity = vertical + turned;

        Vector3 facing = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        if (facing.sqrMagnitude > 0.01f)
        {
            stableFacing = facing.normalized;
        }
    }

    /// <summary>
    /// Keeps the direction of travel inside the heading cone around the road direction.
    /// The angle is measured in the plane of the road, so a slope does not count against it.
    /// </summary>
    private void LimitHeading()
    {
        if (headingCone <= 0f || body.isKinematic)
        {
            return;
        }

        if (!road.TryGetSurfaceCoord(body.position, roadHintT, out float t, out float u, out _))
        {
            return;
        }

        roadHintT = t;
        if (!road.TryGetSurface(t, u, out _, out Vector3 tangent, out Vector3 roadUp))
        {
            return;
        }

        Vector3 velocity = body.linearVelocity;
        Vector3 vertical = Vector3.Project(velocity, roadUp);
        Vector3 planar = velocity - vertical;
        Vector3 forward = Vector3.ProjectOnPlane(tangent, roadUp);
        float speed = planar.magnitude;
        // A sled that is nearly stopped or sliding back has no heading to hold.
        // Turning it around here would also hide the backward slide that ends the run.
        if (speed < 2f || forward.sqrMagnitude < 0.0001f || Vector3.Dot(planar, forward) <= 0f)
        {
            return;
        }

        float halfCone = headingCone * 0.5f;
        float angle = Vector3.SignedAngle(forward, planar, roadUp);
        if (Mathf.Abs(angle) <= halfCone)
        {
            return;
        }

        Vector3 limited = Quaternion.AngleAxis(Mathf.Sign(angle) * halfCone, roadUp) * forward.normalized;
        body.linearVelocity = vertical + limited * speed;

        Vector3 facing = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        if (facing.sqrMagnitude > 0.01f)
        {
            stableFacing = facing.normalized;
        }
    }

    /// <summary>
    /// Turns the body so the capsule points along the heading and lies flat on the slope.
    /// The solver never rotates the body, so without this the capsule would keep its launch direction.
    /// </summary>
    private void AlignBody()
    {
        if (body.isKinematic)
        {
            return;
        }

        // In the air the last tilt is kept. There is no surface to lie on, and leveling out
        // would swing the nose down into the landing.
        float reach = radius + 1f;
        if (road.SurfaceCollider.Raycast(new Ray(body.position, -bodyUp), out RaycastHit ground, reach))
        {
            // Eased, so the edge between two road triangles does not snap the capsule over.
            float blend = 1f - Mathf.Exp(-12f * Time.fixedDeltaTime);
            bodyUp = Vector3.Slerp(bodyUp, ground.normal, blend).normalized;
        }

        Vector3 heading = Vector3.ProjectOnPlane(body.linearVelocity, bodyUp);
        if (heading.sqrMagnitude < 1f)
        {
            heading = Vector3.ProjectOnPlane(body.rotation * Vector3.forward, bodyUp);
        }

        if (heading.sqrMagnitude < 0.0001f)
        {
            return;
        }

        body.rotation = Quaternion.LookRotation(heading.normalized, bodyUp);
    }

    private void Hold(Vector3 position)
    {
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        // A kinematic body rejects velocity writes and logs an error on every call.
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        body.isKinematic = true;
        body.position = position;
    }

    private void SampleRide()
    {
        if (body == null)
        {
            return;
        }

        if (road != null && road.TryGetSurfaceCoord(body.position, roadHintT, out float t, out float u, out _))
        {
            roadHintT = t;
            distanceMeters = Mathf.Max(0f, t * road.RoadLength());
            if (distanceMeters > runMaxMeters)
            {
                runMaxMeters = distanceMeters;
            }

            if (road.TryGetSurface(t, u, out _, out _, out Vector3 up) && up.sqrMagnitude > 0.0001f)
            {
                surfaceNormal = up.normalized;
            }
        }

        // Update runs after the physics step, so this flag is the contact from that step.
        if (roadTouched && roadContactNormal.sqrMagnitude > 0.0001f)
        {
            surfaceNormal = roadContactNormal.normalized;
        }

        float speed = Vector3.ProjectOnPlane(body.linearVelocity, surfaceNormal).magnitude;
        surfaceSpeed = speed < 0.25f ? 0f : speed;
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

    private void UpdateSlideSurface(bool clearTrail)
    {
        if (road == null || body == null)
        {
            return;
        }

        if (!road.TryGetSurfaceCoord(body.position, roadHintT, out float t, out float u, out Vector3 tangent))
        {
            return;
        }

        roadHintT = t;
        road.ApplyContactFriction(road.FrictionAt(t, u) * economy.FrictionScale);

        if (!clearTrail || !IsOnRoad(t, u, out Vector3 contactPoint, out Vector3 contactNormal))
        {
            trailValid = false;
            return;
        }

        // Sample coverage before the clear. The disc sits behind the ball so the next
        // friction sample under it still sees the snow it is rolling on.
        float coverage = road.SnowCoverage(t, u);
        float clearRadius = Mathf.Max(radius, 0.05f);
        Vector3 behind = body.position - tangent * (clearRadius * 1.7f);
        if (!trailValid)
        {
            lastTrailPoint = behind;
        }

        road.ClearSnowAlong(lastTrailPoint, behind, clearRadius, t);
        lastTrailPoint = behind;
        trailValid = true;
        EmitSnowSpray(contactPoint, contactNormal, tangent, coverage);
    }

    private bool IsOnRoad(float t, float u, out Vector3 point, out Vector3 normal)
    {
        point = body.position;
        normal = Vector3.up;

        bool touching = hadRoadContact;
        if (touching)
        {
            point = roadContactPoint;
            normal = roadContactNormal.sqrMagnitude > 0.0001f ? roadContactNormal : Vector3.up;
        }

        float gap = float.PositiveInfinity;
        Vector3 surface = point;
        Vector3 surfaceUp = normal;
        if (road.TryGetSurface(t, u, out surface, out _, out surfaceUp))
        {
            gap = Vector3.Dot(body.position - surface, surfaceUp);
        }

        if (touching)
        {
            groundedGraceUntil = Time.time + 0.06f;
        }

        // A jump has no contact and sits above the surface. Grace only covers a missed
        // contact while the ball is still against the road, not the flight itself.
        bool resting = gap <= radius + 0.4f;
        bool grace = Time.time <= groundedGraceUntil && gap <= radius + 0.85f;
        if (!touching && !resting && !grace)
        {
            return false;
        }

        if (!touching)
        {
            point = surface;
            normal = surfaceUp.sqrMagnitude > 0.0001f ? surfaceUp : Vector3.up;
        }

        return true;
    }

    private void EmitSnowSpray(Vector3 point, Vector3 normal, Vector3 tangent, float coverage)
    {
        if (coverage < 0.2f || body == null)
        {
            return;
        }

        float speed = body.linearVelocity.magnitude;
        if (speed < 1.5f)
        {
            return;
        }

        if (!snowSpray.isPlaying)
        {
            snowSpray.Play();
        }

        sprayBudget += speed * Mathf.Clamp01(coverage) * Time.fixedDeltaTime * 8f;
        int count = Mathf.Min(Mathf.FloorToInt(sprayBudget), 6);
        if (count <= 0)
        {
            return;
        }

        sprayBudget -= count;
        if (normal.sqrMagnitude < 0.0001f)
        {
            normal = Vector3.up;
        }
        else
        {
            normal.Normalize();
        }

        if (tangent.sqrMagnitude > 0.0001f)
        {
            tangent.Normalize();
        }

        Vector3 side = Vector3.Cross(normal, tangent);
        if (side.sqrMagnitude < 0.0001f)
        {
            side = Vector3.Cross(normal, Vector3.forward);
        }

        side.Normalize();

        float kick = Mathf.Lerp(2.2f, 6f, Mathf.Clamp01(speed / 28f));
        // Faster sled, bigger flakes. Each flake keeps the size it was thrown with,
        // so the plume shrinks as the sled slows down.
        float size = Mathf.Lerp(spraySizeSlow, spraySizeFast, Mathf.Clamp01(speed / Mathf.Max(1f, sprayFullSpeed)));
        for (int i = 0; i < count; i++)
        {
            float sideOffset = Random.Range(-radius * 0.85f, radius * 0.85f);
            var emit = new ParticleSystem.EmitParams
            {
                position = point + normal * 0.35f - tangent * (radius * 0.35f) + side * sideOffset,
                velocity = tangent * (speed * 0.22f)
                    + normal * Random.Range(kick * 0.45f, kick)
                    + side * Random.Range(-kick * 0.7f, kick * 0.7f),
                startSize = Random.Range(0.08f, 0.3f) * size,
                startLifetime = Random.Range(0.28f, 0.65f),
                startColor = new Color(0.96f, 0.98f, 1f, Random.Range(0.7f, 0.95f))
            };
            snowSpray.Emit(emit, 1);
        }
    }

    private void ResetToStart()
    {
        // A restart in the middle of a ride still closes that run, so its coins are banked.
        if ((launched || pendingLaunch) && !stopped)
        {
            RunFinished?.Invoke();
        }

        PlayArmed = false;
        RunSerial++;
        RunReset?.Invoke();
        if (runMaxMeters > 1f)
        {
            previousMaxMeters = runMaxMeters;
        }

        launched = false;
        aiming = false;
        aimTouchId = -1;
        pendingLaunch = false;
        stopped = false;
        SteerInput = 0f;
        steer = 0f;
        steerVelocity = 0f;
        movedForward = false;
        restTimer = 0f;
        pendingVelocity = Vector3.zero;
        distanceMeters = 0f;
        runMaxMeters = 0f;
        surfaceSpeed = 0f;
        surfaceNormal = Vector3.up;
        trailValid = false;
        roadHintT = -1f;
        groundedGraceUntil = 0f;
        roadTouched = false;
        hadRoadContact = false;
        sprayBudget = 0f;
        if (snowSpray != null)
        {
            snowSpray.Clear(true);
        }

        SetLines(false);
        placed = TryPlace();
    }

    private bool TryPlace()
    {
        if (!road.TryGetSurface(0f, 0.5f, out Vector3 point, out Vector3 tangent, out Vector3 up))
        {
            return false;
        }

        if (up.sqrMagnitude < 0.0001f)
        {
            up = Vector3.up;
        }

        anchor = point + up.normalized * radius;

        Vector3 planar = Vector3.ProjectOnPlane(tangent, Vector3.up);
        stableFacing = planar.sqrMagnitude > 0.001f ? planar.normalized : Vector3.forward;

        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        body.isKinematic = true;
        bodyUp = up.normalized;
        Vector3 along = Vector3.ProjectOnPlane(tangent, bodyUp);
        body.rotation = along.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(along.normalized, bodyUp)
            : Quaternion.identity;
        body.position = anchor;

        if (visual != null && stableFacing.sqrMagnitude > 0.001f)
        {
            visual.rotation = Quaternion.LookRotation(stableFacing, Vector3.up);
        }

        return true;
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
        if (road == null || !road.TryGetSurface(0f, 0.5f, out _, out Vector3 tangent, out Vector3 up))
        {
            return stableFacing;
        }

        Vector3 axis = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
        Vector3 roadForward = Vector3.ProjectOnPlane(tangent, axis);
        if (roadForward.sqrMagnitude < 0.0001f)
        {
            return stableFacing;
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
