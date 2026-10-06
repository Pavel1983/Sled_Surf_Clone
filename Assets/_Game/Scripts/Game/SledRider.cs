using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Puts Ladybug on the sled and plays her poses.
/// She stands facing the camera on the start screen, turns and sits down when TAP TO PLAY is pressed,
/// slides, and falls on a crash.
/// While she is seated her hips are pinned to the visual origin.
/// <see cref="SlideFacing"/> puts that origin on the snow and tilts it with the slope.
/// </summary>
[DisallowMultipleComponent]
public class SledRider : MonoBehaviour
{
    // Mixer inputs.
    private const int SlideInput = 0;
    private const int CrashInput = 1;
    private const int IdleInput = 2;
    private const int SitInput = 3;

    [Tooltip("Child of the sled the model is parented to. SlideFacing moves and tilts it.")]
    [SerializeField] private Transform visual;
    [Tooltip("The Ladybug model. A copy is placed under Visual when the scene starts.")]
    [SerializeField] private GameObject modelPrefab;
    [SerializeField] private AnimationClip slideClip;
    [Tooltip("Standing pose for the start screen.")]
    [SerializeField] private AnimationClip idleClip;
    [Tooltip("The drop from standing into the slide pose.")]
    [SerializeField] private AnimationClip sitClip;
    [SerializeField] private AnimationClip crashClip;
    [Tooltip("The camera she turns to while she stands on the start screen.")]
    [SerializeField] private Transform viewCamera;
    [Tooltip("Height of the model in meters. Twice a real person so she reads next to the sled sphere.")]
    [SerializeField] private float targetHeight = 3.24f;
    [Tooltip("Extra yaw if the imported model does not face the sled's forward axis.")]
    [SerializeField] private float yawOffset = 0f;
    [Tooltip("Seconds the slide pose takes to melt into the crash fall.")]
    [SerializeField] private float crashBlendTime = 0.15f;
    [Tooltip("Seconds the standing pose takes to melt into the sit-down, and the sit-down into the slide.")]
    [SerializeField] private float sitBlendTime = 0.15f;
    [Tooltip("Part of the sit-down she spends turning from the camera to the road. 1 turns for the whole sit-down.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float turnShare = 0.7f;

    private Transform rider;
    private Transform hips;
    private SkinnedMeshRenderer skin;
    private PlayableGraph graph;
    private bool graphAlive;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable crashPlayable;
    private AnimationClipPlayable idlePlayable;
    private AnimationClipPlayable sitPlayable;
    private bool hasIdleClip;
    private bool hasSitClip;
    private float idleLength;
    private float sitLength;

    // 0 is standing at the start, 1 is seated on the sled.
    private float seat = 1f;
    private bool sittingDown;

    // Yaw that faces the camera, kept from the last frame she was fully standing.
    private float standYaw;
    private SlingshotLaunch launch;
    private bool hasCrashClip;
    private bool crashed;
    private float crashWeight;
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
        PlaySlide();

        launch = GetComponent<SlingshotLaunch>();
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

        UpdateSeat();
        BlendCrash();
        ApplyWeights();
        // Before the pin: turning her moves the pelvis the pin reads.
        FaceCamera();
        // The fall starts on her feet and ends on the snow. Pinning the pelvis would hold her
        // hips in place and swing the rest of the body around them.
        if (!crashed)
        {
            PinHips();
        }
    }

    private void OnDestroy()
    {
        if (graphAlive && graph.IsValid())
        {
            graph.Destroy();
        }
    }

    /// <summary>Starts the fall. The pose blends in from whatever she was doing.</summary>
    public void PlayCrash()
    {
        if (!hasCrashClip || !graphAlive)
        {
            return;
        }

        crashed = true;
        crashPlayable.SetTime(0);
        crashPlayable.SetDone(false);
        crashPlayable.Play();
    }

    /// <summary>Drops the fall, so the next run starts from the standing pose.</summary>
    public void StopCrash()
    {
        if (!crashed)
        {
            return;
        }

        crashed = false;
        crashWeight = 0f;
        crashPlayable.Pause();
    }

    private void UpdateSeat()
    {
        if (!hasIdleClip || launch == null)
        {
            return;
        }

        bool wantSeated = launch.PlayArmed || launch.IsRiding || launch.IsStopped;
        if (wantSeated && seat < 1f)
        {
            float duration = hasSitClip ? sitLength : sitBlendTime;
            seat = duration > 0.001f ? Mathf.MoveTowards(seat, 1f, Time.deltaTime / duration) : 1f;
            sittingDown = true;
        }
        else if (!wantSeated && seat > 0f)
        {
            // The run has jumped back to the start and the camera cut with it, so she is simply
            // standing again. A blend from the seated pose would drag her feet through the snow.
            seat = 0f;
            sittingDown = false;
        }

        // The pack imports the idle without looping, so a playable would freeze on its last frame.
        double idleTime = idlePlayable.GetTime();
        if (idleTime >= idleLength)
        {
            idlePlayable.SetTime(idleTime - idleLength);
        }

        // The sit-down is scrubbed by hand so the pose and the hip pin move together.
        if (hasSitClip)
        {
            sitPlayable.SetTime(seat * sitLength);
        }
    }

    private void FaceCamera()
    {
        if (!hasIdleClip || crashed)
        {
            return;
        }

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

    private void ApplyWeights()
    {
        if (!graphAlive)
        {
            return;
        }

        float idle = 1f - seat;
        float sit = 0f;
        float slide = seat;
        if (sittingDown && hasSitClip)
        {
            float elapsed = seat * sitLength;
            float blend = Mathf.Max(0.01f, Mathf.Min(sitBlendTime, sitLength * 0.5f));
            float into = Mathf.Clamp01(elapsed / blend);
            float outOf = Mathf.Clamp01((elapsed - (sitLength - blend)) / blend);
            idle = 1f - into;
            sit = into * (1f - outOf);
            slide = into * outOf;
        }

        float rest = 1f - crashWeight;
        mixer.SetInputWeight(SlideInput, slide * rest);
        mixer.SetInputWeight(CrashInput, crashWeight);
        mixer.SetInputWeight(IdleInput, idle * rest);
        mixer.SetInputWeight(SitInput, sit * rest);
    }

    private void BlendCrash()
    {
        if (!crashed || crashWeight >= 1f)
        {
            return;
        }

        crashWeight = crashBlendTime > 0.001f
            ? Mathf.MoveTowards(crashWeight, 1f, Time.deltaTime / crashBlendTime)
            : 1f;
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

    private void PlaySlide()
    {
        AnimationClip clip = slideClip;
        Animator animator = rider.GetComponentInChildren<Animator>();
        if (clip == null || animator == null || animator.avatar == null)
        {
            return;
        }

        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.runtimeAnimatorController = null;

        graph = PlayableGraph.Create("SledSlide");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyFootIK(false);
        mixer = AnimationMixerPlayable.Create(graph, 4);
        graph.Connect(playable, 0, mixer, SlideInput);
        mixer.SetInputWeight(SlideInput, 1f);

        if (idleClip != null)
        {
            idlePlayable = AnimationClipPlayable.Create(graph, idleClip);
            idlePlayable.SetApplyFootIK(false);
            graph.Connect(idlePlayable, 0, mixer, IdleInput);
            idleLength = Mathf.Max(0.01f, idleClip.length);
            hasIdleClip = true;
            // She starts on her feet.
            seat = 0f;
            mixer.SetInputWeight(SlideInput, 0f);
            mixer.SetInputWeight(IdleInput, 1f);
        }

        if (hasIdleClip && sitClip != null)
        {
            sitPlayable = AnimationClipPlayable.Create(graph, sitClip);
            sitPlayable.SetApplyFootIK(false);
            sitPlayable.Pause();
            graph.Connect(sitPlayable, 0, mixer, SitInput);
            sitLength = Mathf.Max(0.01f, sitClip.length);
            hasSitClip = true;
        }

        if (crashClip != null)
        {
            crashPlayable = AnimationClipPlayable.Create(graph, crashClip);
            crashPlayable.SetApplyFootIK(false);
            // With a duration the clip stops on its last frame and she stays down.
            crashPlayable.SetDuration(crashClip.length);
            crashPlayable.Pause();
            graph.Connect(crashPlayable, 0, mixer, CrashInput);
            mixer.SetInputWeight(CrashInput, 0f);
            hasCrashClip = true;
        }

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Slide", animator);
        output.SetSourcePlayable(mixer);
        graph.Play();
        graphAlive = true;
    }
}
