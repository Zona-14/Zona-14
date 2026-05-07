// SPDX-License-Identifier: MIT
// Ported from RMC-14 Content.Shared/_RMC14/Weapons/Ranged/Prediction/PredictedProjectileClientComponent.cs@5e2420d6f2.
using Robust.Shared.Map;

namespace Content.Shared._Zona14.Weapons.Ranged.Prediction;

[RegisterComponent]
public sealed partial class PredictedProjectileClientComponent : Component
{
    [DataField]
    public bool Hit;

    [DataField]
    public EntityCoordinates? Coordinates;
}
