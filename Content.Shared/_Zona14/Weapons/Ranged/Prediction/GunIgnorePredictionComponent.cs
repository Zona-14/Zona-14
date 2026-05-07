// SPDX-License-Identifier: MIT
// Ported from RMC-14 Content.Shared/_RMC14/Weapons/Ranged/Prediction/GunIgnorePredictionComponent.cs@cafb56711d.
using Robust.Shared.GameStates;

namespace Content.Shared._Zona14.Weapons.Ranged.Prediction;

[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedGunPredictionSystem))]
public sealed partial class GunIgnorePredictionComponent : Component;
