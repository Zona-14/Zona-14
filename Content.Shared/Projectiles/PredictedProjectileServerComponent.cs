using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Shared.Projectiles;

/// <summary>
/// Added by the server GunSystem ShootProjectile override to every bullet.
/// Carries the shooter entity so the client can identify bullets fired by the
/// local player and hide them — the client already shows its own predicted version.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PredictedProjectileServerComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? ClientEnt;
}
