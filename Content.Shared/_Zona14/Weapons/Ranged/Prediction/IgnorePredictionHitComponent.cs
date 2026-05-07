// SPDX-License-Identifier: MIT
// Ported from RMC-14 Content.Shared/_RMC14/Weapons/Ranged/Prediction/IgnorePredictionHitComponent.cs@9fba041ad2.
using Robust.Shared.GameStates;

namespace Content.Shared._Zona14.Weapons.Ranged.Prediction;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedGunPredictionSystem))]
public sealed partial class IgnorePredictionHitComponent : Component;
