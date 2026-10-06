using UnityEngine;

/// <summary>
/// An obstacle placed by hand on the road.
/// It is a solid collider: the sled hits it only when it really touches it, so a jump can clear it.
/// The sled reports the touch from its OnCollisionEnter, so the collider can sit on any child.
/// Each obstacle fires once per run and comes back when the next run starts.
/// </summary>
[DisallowMultipleComponent]
public abstract class Obstacle : MonoBehaviour
{
    [Tooltip("Visible part of the obstacle. Hidden after a hit when Hide On Hit is set.")]
    [SerializeField] private GameObject model;
    [SerializeField] private bool hideOnHit = true;

    private Collider[] colliders;
    private bool used;

    private void Awake()
    {
        colliders = GetComponentsInChildren<Collider>(true);
        if (colliders.Length == 0)
        {
            Debug.LogWarning($"{name}: no collider on the obstacle or its children, the sled will pass through.", this);
        }
    }

    /// <summary>The sled touched this obstacle. Called by the sled from OnCollisionEnter.</summary>
    public void Hit(SlingshotLaunch sled)
    {
        if (used)
        {
            return;
        }

        used = true;
        // A used obstacle must not block the sled again, even when its model stays visible.
        SetSolid(false);
        if (hideOnHit)
        {
            SetModelVisible(false);
        }

        Apply(sled);
    }

    /// <summary>Puts the obstacle back for the next run.</summary>
    public void Restore()
    {
        used = false;
        SetModelVisible(true);
        SetSolid(true);
    }

    /// <summary>What the obstacle does to the sled. Called once per run.</summary>
    protected abstract void Apply(SlingshotLaunch sled);

    private void SetSolid(bool solid)
    {
        foreach (Collider part in colliders)
        {
            if (part != null)
            {
                part.enabled = solid;
            }
        }
    }

    private void SetModelVisible(bool visible)
    {
        if (model != null && model.activeSelf != visible)
        {
            model.SetActive(visible);
        }
    }
}
