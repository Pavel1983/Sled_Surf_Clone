using UnityEngine;

/// <summary>
/// The sound clips of a run, in one asset that <see cref="RunAudio"/> points at.
/// </summary>
public class RunSounds : ScriptableObject
{
    [Range(0f, 1f)] public float Volume = 1f;

    [Header("Obstacles")]
    [Tooltip("Impact with an obstacle that slows the sled.")]
    public AudioClip SlowHit;
    [Tooltip("One of these is picked at random on the same impact.")]
    public AudioClip[] Ouch;
    [Tooltip("Impact with an obstacle that ends the run.")]
    public AudioClip CrashHit;
    [Tooltip("Played together with Crash Hit.")]
    public AudioClip Death;

    [Header("Ride")]
    [Tooltip("She sits down after TAP TO PLAY.")]
    public AudioClip SitDown;
    [Tooltip("A quick swing of the stick from one side to the other on the snow.")]
    public AudioClip SharpTurn;
    [Tooltip("Back on the road after a jump.")]
    public AudioClip Land;
    public AudioClip Coin;

    [Header("Interface")]
    public AudioClip UiTap;
}
