using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The sled: a non-rotating body that waits at the start of a <see cref="SplineRoad"/>, is shot down it,
/// and slides until the run ends. This class owns the body and the state of the run.
/// Aiming, steering and the snow trail are separate components on the same object, and the sled
/// calls them in a fixed order on each step.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
[RequireComponent(typeof(SlingshotPull), typeof(SledSteering), typeof(SnowTrail))]
public class Sled : MonoBehaviour
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
    [Tooltip("Child the rider model hangs on. It is turned to face down the road when the sled is put on the start pad.")]
    [SerializeField] private Transform visual;

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

    private Rigidbody body;

    // Velocity going into the physics step. A collision callback only sees the velocity after the impact.
    private Vector3 velocityBeforeStep;
    private CapsuleCollider capsule;
    private SlingshotPull pull;
    private SledSteering steering;
    private SnowTrail trail;
    private RunEconomy economy;

    // Radius of the capsule. The seat rides this far above the road.
    private float radius;
    private Vector3 bodyUp = Vector3.up;
    private Vector3 anchor;
    private Vector3 stableFacing = Vector3.forward;
    private bool placed;
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

    // Last normalized position on the road. Keeps the next spline query in a short window.
    private float roadHintT = -1f;
    private float groundedGraceUntil;
    private bool roadTouched;
    private bool hadRoadContact;
    private Vector3 roadContactPoint;
    private Vector3 roadContactNormal = Vector3.up;
    private float resultsTime;

    public Rigidbody Body => body;
    public Vector3 StableFacing => stableFacing;
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

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        pull = GetComponent<SlingshotPull>();
        steering = GetComponent<SledSteering>();
        trail = GetComponent<SnowTrail>();
        radius = capsule.radius;
        pull.Released += Launch;
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

        pull.ReadPull(anchor);
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
                Steer();
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

    private void Launch(Vector3 direction, float speed)
    {
        pendingVelocity = direction * (speed * economy.LaunchScale);
        pendingLaunch = true;
        economy.BeginRun();
        stableFacing = direction;
    }

    /// <summary>
    /// One steering step: ease the stick, turn the velocity, then keep it inside the heading cone.
    /// The sled supplies what the steering cannot know: whether it is on the road and how the road lies.
    /// </summary>
    private void Steer()
    {
        if (body.isKinematic)
        {
            return;
        }

        steering.Smooth(Time.fixedDeltaTime);
        if (steering.IsTurning)
        {
            bool grounded = false;
            Vector3 axis = Vector3.up;
            if (road.TryGetSurfaceCoord(body.position, roadHintT, out float t, out float u, out _))
            {
                roadHintT = t;
                grounded = IsOnRoad(t, u, out _, out Vector3 normal);
                if (grounded && normal.sqrMagnitude > 0.0001f)
                {
                    axis = normal.normalized;
                }
            }

            if (steering.Turn(body, grounded, axis, Time.fixedDeltaTime))
            {
                RememberFacing();
            }
        }

        if (!steering.LimitsHeading)
        {
            return;
        }

        if (!road.TryGetSurfaceCoord(body.position, roadHintT, out float roadT, out float roadU, out _))
        {
            return;
        }

        roadHintT = roadT;
        if (road.TryGetSurface(roadT, roadU, out _, out Vector3 tangent, out Vector3 roadUp)
            && steering.LimitHeading(body, tangent, roadUp))
        {
            RememberFacing();
        }
    }

    private void RememberFacing()
    {
        Vector3 facing = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        if (facing.sqrMagnitude > 0.01f)
        {
            stableFacing = facing.normalized;
        }
    }

    private void FinishRun()
    {
        stopPosition = body.position;
        stopped = true;
        resultsTime = Time.time;
        restTimer = 0f;
        steering.Clear();
        Hold(stopPosition);
        RunFinished?.Invoke();
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
            trail.Break();
            return;
        }

        trail.Leave(road, body, t, u, tangent, contactPoint, contactNormal, radius);
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
        pendingLaunch = false;
        stopped = false;
        pull.Cancel();
        steering.Clear();
        trail.Clear();
        movedForward = false;
        restTimer = 0f;
        pendingVelocity = Vector3.zero;
        distanceMeters = 0f;
        runMaxMeters = 0f;
        surfaceSpeed = 0f;
        surfaceNormal = Vector3.up;
        roadHintT = -1f;
        groundedGraceUntil = 0f;
        roadTouched = false;
        hadRoadContact = false;
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

}
