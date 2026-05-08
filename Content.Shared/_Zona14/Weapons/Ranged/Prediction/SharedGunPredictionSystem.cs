// SPDX-License-Identifier: MIT
// Ported from RMC-14 Content.Shared/_RMC14/Weapons/Ranged/Prediction/SharedGunPredictionSystem.cs@2f5dc02e44.
// Zona14 deviation: RMC's master `rmc.gun_prediction` CVar was dropped — prediction is always on.
using Content.Shared.CombatMode;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Shared._Zona14.Weapons.Ranged.Prediction;

public abstract class SharedGunPredictionSystem : EntitySystem
{
    [Dependency] private readonly SharedCombatModeSystem _combatMode = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;

    public List<EntityUid>? ShootRequested(NetEntity netGun, NetCoordinates coordinates, NetEntity? target,
        List<int>? projectiles, ICommonSession session)
    {
        var user = session.AttachedEntity;
        if (user == null ||
            !_combatMode.IsInCombatMode(user) ||
            !_gun.TryGetGun(user.Value, out var ent, out var gun))
        {
            return null;
        }

        if (ent != GetEntity(netGun))
            return null;

#pragma warning disable RA0002
        gun.ShootCoordinates = GetCoordinates(coordinates);
        gun.Target = GetEntity(target);
#pragma warning restore RA0002
        return _gun.AttemptShoot(user.Value, ent, gun, projectiles, session);
    }
}
