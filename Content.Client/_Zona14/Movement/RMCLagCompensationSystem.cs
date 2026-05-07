// SPDX-License-Identifier: MIT
// Ported from RMC-14 Content.Client/_RMC14/Movement/RMCLagCompensationSystem.cs@2f5dc02e44.
using Content.Shared._Zona14.Movement;
using Robust.Client.Timing;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Client._Zona14.Movement;

public sealed class RMCLagCompensationSystem : SharedRMCLagCompensationSystem
{
    [Dependency] private readonly IClientGameTiming _timing = default!;

    public override GameTick GetLastRealTick(NetUserId? session)
        => _timing.LastRealTick;
}
