using UnityEngine;

/// <summary>
/// Puts Ladybug on the sled and plays her poses.
/// She stands facing the camera on the start screen, turns and sits down when TAP TO PLAY is pressed,
/// slides, and falls on a crash.
/// While she is seated her hips are pinned to the visual origin.
/// <see cref="SlideFacing"/> puts that origin on the snow and tilts it with the slope.
/// The poses and the blends between them are in the animator controller. This class only sets its
/// two parameters and reads back how far the sit-down has got.
/// </summary>
[DisallowMultipleComponent]
public class SledRider : MonoBehaviour
{
    private static readonly int SeatedParameter = Animator.StringToHash("Seated");
    private static readonly int CrashedParameter = Animator.StringToHash("Crashed");
    private static readonly int IdleState = Animator.StringToHash("Idle");
    private static readonly int SitState = Animator.StringToHash("Sit");

    [Tooltip("Child of the sled the model is parented to. SlideFacing moves and tilts it.")]
    [SerializeField] private Transform visual;
    [Tooltip("The Ladybug model. A copy is placed under Visual when the scene starts.")]
    [SerializeField] private GameObject modelPrefab;
    [Tooltip("States Idle, Sit, Slide and Crash, switched by the Seated and Crashed parameters.")]
    [SerializeField] private RuntimeAnimatorController controller;
    [Tooltip("The camera she turns to while she stands on the start screen.")]
    [SerializeField] private Transform viewCamera;
    [Tooltip("Height of the model in meters. Twice a real person so she reads next to the sled sphere.")]
    [SerializeField] private float targetHeight = 3.24f;
    [Tooltip("Extra yaw if the imported model does not face the sled's forward axis.")]
    [SerializeField] private float yawOffset = 0f;
    [Tooltip("Part of the sit-down she spends turning from the camera to the road. 1 turns for the whole sit-down.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float turnShare = 0.7f;

    private Transform rider;
    private Transform hips;
    private SkinnedMeshRenderer skin;
    private Animator animator;

    // True from TAP TO PLAY until the run returns to the start.
    private bool seated;

    // 0 is standing at the start, 1 is seated on the sled. Read from the sit-down state every frame.
    private float seat;

    // Yaw that faces the camera, kept from the last frame she was fully standing.
    private float standYaw;
    private bool crashed;
    private int posedFrames;
    private bool heightFitted;

    private void Awake()
    {
        rider = CreateRider(modelPrefab, visual);
        rider.localPosition = Vector3.zero;
        rider.localRotation = Quaternion.Euler(0f, yawOffset, 0f);
        rider.localScale = Vector3.one;

        Collider[] colliders = rider.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }

        skin = rider.GetComponentInChildren<SkinnedMeshRenderer>();
        heightFitted = FitHeight();

        animator = rider.GetComponentInChildren<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.runtimeAnimatorController = controller;
    }

    private void LateUpdate()
    {
        if (rider == null || skin == null)
        {
            return;
        }

        if (!heightFitted)
        {
            heightFitted = FitHeight();
        }

        if (!heightFitted)
        {
            return;
        }

        posedFrames++;
        if (posedFrames < 2)
        {
            return;
        }

        // The fall starts on her feet and ends on the snow. Pinning the pelvis would hold her
        // hips in place and swing the rest of the body around them.
        if (crashed)
        {
            return;
        }

        seat = ReadSeat();
        // Before the pin: turning her moves the pelvis the pin reads.
        FaceCamera();
        PinHips();
    }

    /// <summary>Starts the turn to the road and the sit-down.</summary>
    public void SitDown()
    {
        seated = true;
        animator.SetBool(SeatedParameter, true);
    }

    /// <summary>Starts the fall. The pose blends in from whatever she was doing.</summary>
    public void PlayCrash()
    {
        crashed = true;
        animator.SetBool(CrashedParameter, true);
    }

    /// <summary>
    /// Puts her back on her feet for the next run. The run has jumped back to the start and the camera
    /// cut with it, so the controller switches to the standing pose without a blend.
    /// </summary>
    public void StandUp()
    {
        seated = false;
        crashed = false;
        seat = 0f;
        animator.SetBool(SeatedParameter, false);
        animator.SetBool(CrashedParameter, false);
    }

    // The animator has already been evaluated this frame, so the turn and the hip pin
    // follow the same moment of the sit-down that is on screen.
    private float ReadSeat()
    {
        if (!seated)
        {
            return 0f;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash == IdleState)
        {
            // Still standing: the sit-down is blending in, or has not started yet.
            return animator.IsInTransition(0)
                ? Mathf.Clamp01(animator.GetNextAnimatorStateInfo(0).normalizedTime)
                : 0f;
        }

        if (state.shortNameHash == SitState)
        {
            return Mathf.Clamp01(state.normalizedTime);
        }

        return 1f;
    }

    private void FaceCamera()
    {
        // The yaw is frozen once she starts to sit. A camera straight behind her is half a turn
        // away either way, and a live value would let the turn flip sides from frame to frame.
        if (seat <= 0f)
        {
            Vector3 toCamera = visual.InverseTransformPoint(viewCamera.position);
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude > 0.0001f)
            {
                standYaw = Mathf.Atan2(toCamera.x, toCamera.z) * Mathf.Rad2Deg;
            }
        }

        float turn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(seat / turnShare));
        float yaw = standYaw + Mathf.DeltaAngle(standYaw, yawOffset) * turn;
        rider.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    private void PinHips()
    {
        if (hips == null)
        {
            hips = FindNamed(rider, "Bip001 Pelvis");
        }

        Vector3 anchor = hips != null ? hips.position : LowestPoint();
        // The slide pose keeps moving the pelvis. Pull it back to the visual origin
        // every frame so the butt stays on the point SlideFacing plants on the snow.
        // Standing, she is not pinned: her feet are on the origin and the pin fades in as she sits.
        Vector3 pinned = rider.localPosition - visual.InverseTransformPoint(anchor);
        rider.localPosition = pinned * seat;
    }

    private Vector3 LowestPoint()
    {
        return skin.bounds.ClosestPoint(visual.position - visual.up * 20f);
    }

    private static Transform FindNamed(Transform root, string boneName)
    {
        if (root.name == boneName)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamed(root.GetChild(i), boneName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static Transform CreateRider(GameObject model, Transform parent)
    {
        // Instantiate(model, parent) casts the clone back to the source type.
        // Cloning an FBX prefab wrapper does not return that type, so the cast throws.
#if UNITY_EDITOR
        if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(model))
        {
            UnityEngine.Object spawned = UnityEditor.PrefabUtility.InstantiatePrefab(model, parent);
            if (spawned is GameObject prefabObject)
            {
                return prefabObject.transform;
            }
        }
#endif
        GameObject copy = UnityEngine.Object.Instantiate(model);
        copy.transform.SetParent(parent, false);
        return copy.transform;
    }

    private bool FitHeight()
    {
        if (skin == null)
        {
            return false;
        }

        rider.localScale = Vector3.one;
        float worldHeight = skin.bounds.size.y;
        if (worldHeight < 0.01f)
        {
            worldHeight = skin.localBounds.size.y;
        }

        if (worldHeight < 0.01f)
        {
            return false;
        }

        rider.localScale = Vector3.one * (targetHeight / worldHeight);
        return true;
    }
}
