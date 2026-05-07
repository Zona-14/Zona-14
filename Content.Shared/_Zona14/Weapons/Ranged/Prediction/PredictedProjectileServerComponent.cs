// SPDX-License-Identifier: MIT
// Ported from RMC-14 Content.Shared/_RMC14/Weapons/Ranged/Prediction/PredictedProjectileServerComponent.cs@a62d14c470.
using Robust.Shared.GameStates;
using Robust.Shared.Player;

namespace Content.Shared._Zona14.Weapons.Ranged.Prediction;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PredictedProjectileServerComponent : Component
{
    public ICommonSession? Shooter;

    [DataField, AutoNetworkedField]
    public int ClientId;

    [DataField, AutoNetworkedField]
    public EntityUid? ClientEnt;

    [DataField]
    public bool Hit;
}
