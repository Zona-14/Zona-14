// SPDX-License-Identifier: MIT
// Ported from RMC-14 Content.Server/_RMC14/Movement/RMCLagCompensationSystem.cs@2f5dc02e44.
using Content.Server.Movement.Systems;
using Content.Shared._Zona14.CCVar;
using Content.Shared._Zona14.Movement;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._Zona14.Movement;

public sealed class RMCLagCompensationSystem : SharedRMCLagCompensationSystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly LagCompensationSystem _lagCompensation = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_config, Zona14CVars.LagCompensationMilliseconds,
            v => _lagCompensation.BufferTime = TimeSpan.FromMilliseconds(v), true);
    }

    public override (EntityCoordinates Coordinates, Angle Angle) GetCoordinatesAngle(EntityUid uid,
        ICommonSession? pSession, TransformComponent? xform = null)
        => _lagCompensation.GetCoordinatesAngle(uid, pSession, xform);

    public override Angle GetAngle(EntityUid uid, ICommonSession? session, TransformComponent? xform = null)
        => _lagCompensation.GetAngle(uid, session, xform);

    public override EntityCoordinates GetCoordinates(EntityUid uid, ICommonSession? session,
        TransformComponent? xform = null)
        => _lagCompensation.GetCoordinates(uid, session, xform);
}
