using Content.Server.Movement.Components;
using Content.Shared.Mobs.Components;
using Robust.Shared.GameObjects;

namespace Content.Server.Movement.Systems;

/// <summary>
/// Ensures every mob gets a <see cref="LagCompensationComponent"/> so that
/// <see cref="LagCompensationSystem"/> records position history for them.
/// This makes the existing melee lag compensation actually work, and enables
/// the new projectile lag compensation.
/// </summary>
public sealed class LagCompEnsureSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateComponent, MapInitEvent>(OnMobMapInit);
    }

    private void OnMobMapInit(EntityUid uid, MobStateComponent _, ref MapInitEvent args)
    {
        EnsureComp<LagCompensationComponent>(uid);
    }
}
