// SPDX-License-Identifier: MIT
// Ported from RMC-14 Content.Shared/_RMC14/Weapons/Ranged/Prediction/PredictedProjectileHitComponent.cs@5e2420d6f2.
using Content.Shared.Projectiles;
using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._Zona14.Weapons.Ranged.Prediction;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedGunPredictionSystem), typeof(SharedProjectileSystem))]
public sealed partial class PredictedProjectileHitComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityCoordinates Origin;

    [DataField, AutoNetworkedField]
    public float Distance;
}
