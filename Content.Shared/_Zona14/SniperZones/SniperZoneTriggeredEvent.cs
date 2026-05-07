// SPDX-License-Identifier: MIT
namespace Content.Shared._Zona14.SniperZones;

public enum SniperZoneAction : byte
{
    Shoot,
    Attack,
    Throw,
}

/// <summary>
///     Raised on the server only after <see cref="SharedSniperZoneSystem"/>
///     cancels an action. <c>STNPCSniperSystem</c> handles this for retaliation
///     damage, gunshot audio, and IC chat — server-only side effects that must
///     not run on client prediction passes.
/// </summary>
[ByRefEvent]
public record struct SniperZoneTriggeredEvent(EntityUid User, Vector2i Coord, SniperZoneAction Action);
