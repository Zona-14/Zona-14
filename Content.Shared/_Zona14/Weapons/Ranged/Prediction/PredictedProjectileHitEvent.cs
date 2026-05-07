// SPDX-License-Identifier: MIT
// Ported from RMC-14 Content.Shared/_RMC14/Weapons/Ranged/Prediction/PredictedProjectileHitEvent.cs@5e2420d6f2.
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Zona14.Weapons.Ranged.Prediction;

[Serializable, NetSerializable]
public sealed class PredictedProjectileHitEvent(int projectile, HashSet<(NetEntity Id, MapCoordinates Coordinates)> hit)
    : EntityEventArgs
{
    public readonly int Projectile = projectile;
    public readonly HashSet<(NetEntity Id, MapCoordinates Coordinates)> Hit = hit;
}
