// SPDX-License-Identifier: MIT
using Robust.Shared.GameStates;

namespace Content.Shared._Zona14.SniperZones;

/// <summary>
///     On a map entity. Tile coordinates (floor-rounded world coords) where
///     shoot/throw/melee are cancelled by the sniper-zone feature. Server
///     populates from STNPCSniperComponent placements on RoundStartedEvent;
///     replicated to clients so the cancel decision stays symmetric.
/// </summary>
/// <remarks>
///     <see cref="ForbiddenCoords"/> is a List for predictable auto-state
///     serialization. Hot-path lookup needs O(1), so <see cref="LookupCache"/>
///     is a non-serialized HashSet rebuilt lazily.
///     <see cref="SharedSniperZoneSystem"/> invalidates the cache on
///     <c>AfterAutoHandleStateEvent</c>.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class SniperZonesComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<Vector2i> ForbiddenCoords = new();

    [ViewVariables]
    public HashSet<Vector2i>? LookupCache;
}
