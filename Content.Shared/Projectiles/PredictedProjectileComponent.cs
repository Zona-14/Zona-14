using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Shared.Projectiles;

/// <summary>
/// Marks a client-side predicted projectile spawned by the client GunSystem.
/// Added by the client ShootProjectile override; never sent to the server.
/// </summary>
[RegisterComponent]
public sealed partial class PredictedProjectileClientComponent : Component
{
    /// <summary>
    /// Saved before each physics solve and restored after resimulation so the
    /// predicted bullet doesn't jump backward when the engine replays past ticks.
    /// </summary>
    [DataField]
    public EntityCoordinates? Coordinates { get; set; }
}
