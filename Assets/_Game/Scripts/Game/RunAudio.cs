using UnityEngine;

/// <summary>
/// Plays the sounds of a run through one audio source.
/// The composition root connects the game events to the Play methods. Landings and sharp turns
/// have no event, so this class watches the sled for them.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class RunAudio : MonoBehaviour
{
    // Stick deflection that counts as steering to one side.
    private const float SideThreshold = 0.35f;
    // The stick has to cross from one side to the other within this many seconds to count as sharp.
    private const float SwingSeconds = 0.45f;
    // Below this speed, in meters per second, a swing does not carve the snow.
    private const float SwingMinSpeed = 6f;
    // Shortest gap between two turn sounds, in seconds.
    private const float SwingCooldown = 0.8f;
    // A hop shorter than this, in seconds, is a bump and not a jump.
    private const float JumpSeconds = 0.3f;

    [SerializeField] private SlingshotLaunch launch;
    [SerializeField] private RunSounds sounds;

    private AudioSource source;
    private int steerSide;
    private float steerSideTime;
    private float nextSwingTime;
    private float airTime;

    private void Awake()
    {
        source = GetComponent<AudioSource>();
    }

    private void Update()
    {
        if (!launch.CanSteer)
        {
            steerSide = 0;
            airTime = 0f;
            return;
        }

        WatchLanding();
        WatchSwing();
    }

    public void PlaySitDown()
    {
        Play(sounds.SitDown);
    }

    /// <summary>The impact of a slowing obstacle, with one of her voice lines over it.</summary>
    public void PlaySlowHit()
    {
        Play(sounds.SlowHit);
        if (sounds.Ouch.Length > 0)
        {
            Play(sounds.Ouch[Random.Range(0, sounds.Ouch.Length)]);
        }
    }

    public void PlayCrash()
    {
        Play(sounds.CrashHit);
        Play(sounds.Death);
    }

    public void PlayCoin()
    {
        Play(sounds.Coin);
    }

    public void PlayClick()
    {
        Play(sounds.UiTap);
    }

    private void WatchLanding()
    {
        if (!launch.IsGrounded)
        {
            airTime += Time.deltaTime;
            return;
        }

        if (airTime >= JumpSeconds)
        {
            Play(sounds.Land);
        }

        airTime = 0f;
    }

    private void WatchSwing()
    {
        float input = launch.SteerInput;
        int side = input > SideThreshold ? 1 : input < -SideThreshold ? -1 : 0;
        if (side == 0)
        {
            return;
        }

        // The time is refreshed while the stick stays on a side, so the window measures
        // how long the stick took to get across the middle.
        bool swung = steerSide != 0 && side != steerSide && Time.time - steerSideTime <= SwingSeconds;
        bool carving = launch.IsGrounded && launch.SurfaceSpeed >= SwingMinSpeed;
        if (swung && carving && Time.time >= nextSwingTime)
        {
            Play(sounds.SharpTurn);
            nextSwingTime = Time.time + SwingCooldown;
        }

        steerSide = side;
        steerSideTime = Time.time;
    }

    private void Play(AudioClip clip)
    {
        if (clip != null)
        {
            source.PlayOneShot(clip, sounds.Volume);
        }
    }
}
