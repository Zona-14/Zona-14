using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using System.Numerics;

namespace Content.Server.Projectiles.Components;

/// <summary>
/// Added to server-side projectiles to store the fire metadata needed for lag compensation.
/// Lets <see cref="Content.Server.Projectiles.ProjectileSystem"/> recover hits that the
/// physics simulation missed because the target moved between the client's fire time and
/// the server bullet's travel time.
/// </summary>
[RegisterComponent]
public sealed partial class ProjectileFiredComponent : Component
{
    /// <summary>Player session that fired this projectile (used for ping-based lag comp lookup).</summary>
    public ICommonSession? FiredBy;

    /// <summary>Server time when fired. Used to compute expected travel distance at despawn.</summary>
    public TimeSpan FiredAt;

    /// <summary>World position (map space) where the bullet was spawned.</summary>
    public Vector2 FiredFromWorldPos;

    /// <summary>Normalised direction the bullet was fired in (map space).</summary>
    public Vector2 FiredDirection;

    /// <summary>Bullet speed in m/s. Used with FiredAt to compute expected travel distance.</summary>
    public float Speed;

    /// <summary>
    /// Entity that the lag-compensated raycast predicted would be hit.
    /// Null if the lag-comp check found no entity in the firing line.
    /// </summary>
    public EntityUid? LagCompTarget;

    /// <summary>Lag-compensated world position of LagCompTarget at fire time.</summary>
    public Vector2 LagCompTargetWorldPos;

    /// <summary>
    /// True once OnStartCollide has processed a valid hit for this bullet.
    /// Prevents the EntityTerminating lag-comp fallback from double-applying damage.
    /// </summary>
    public bool PhysicsHitOccurred;
}
