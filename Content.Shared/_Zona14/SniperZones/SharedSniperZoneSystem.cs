// SPDX-License-Identifier: MIT
using System.Numerics;
using Content.Shared.Interaction.Events;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameStates;
using Robust.Shared.Network;

namespace Content.Shared._Zona14.SniperZones;

public sealed class SharedSniperZoneSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<SniperZonesComponent> _zonesQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        base.Initialize();

        _zonesQuery = GetEntityQuery<SniperZonesComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        // Component-targeted: fires only on entities carrying SniperZoneCheckComponent.
        // The three events all raise on the user/attacker/thrower, so the marker
        // catches every relevant call. No `broadcast: true` needed on any raise site.
        SubscribeLocalEvent<SniperZoneCheckComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<SniperZoneCheckComponent, AttackAttemptEvent>(OnAttackAttempted);
        SubscribeLocalEvent<SniperZoneCheckComponent, BeforeThrowEvent>(OnBeforeThrow);

        // Invalidate the lookup cache when replicated state arrives on the client.
        SubscribeLocalEvent<SniperZonesComponent, AfterAutoHandleStateEvent>(OnZonesStateApplied);
    }

    // SS14 tile convention: floor-round world coords. Math.Floor handles negatives correctly
    // (unlike (int) cast which truncates toward zero). RegenerateMap on the server uses the
    // same conversion so populate and lookup agree at boundaries.
    private static Vector2i WorldToTile(Vector2 worldPos)
        => new((int) MathF.Floor(worldPos.X), (int) MathF.Floor(worldPos.Y));

    private bool TryFindZoneCoord(EntityUid user, out Vector2i coord)
    {
        coord = default;
        if (!_xformQuery.TryComp(user, out var xform))
            return false;
        if (xform.MapUid is not { } map || !_zonesQuery.TryComp(map, out var zones))
            return false;

        coord = WorldToTile(_transform.GetWorldPosition(xform));

        zones.LookupCache ??= new HashSet<Vector2i>(zones.ForbiddenCoords);
        return zones.LookupCache.Contains(coord);
    }

    private void OnZonesStateApplied(Entity<SniperZonesComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        ent.Comp.LookupCache = null;
    }

    private void OnShotAttempted(Entity<SniperZoneCheckComponent> ent, ref ShotAttemptedEvent ev)
    {
        if (ev.Cancelled || !TryFindZoneCoord(ent, out var coord))
            return;

        ev.Cancel();

        if (_net.IsServer)
        {
            var triggered = new SniperZoneTriggeredEvent(ent, coord, SniperZoneAction.Shoot);
            RaiseLocalEvent(ref triggered);
        }
    }

    // AttackAttemptEvent is a class extending CancellableEntityEventArgs.
    // Class events use the legacy (uid, comp, args) handler form — no `ref`.
    private void OnAttackAttempted(EntityUid uid, SniperZoneCheckComponent comp, AttackAttemptEvent ev)
    {
        if (ev.Cancelled || !TryFindZoneCoord(uid, out var coord))
            return;

        ev.Cancel();

        if (_net.IsServer)
        {
            var triggered = new SniperZoneTriggeredEvent(uid, coord, SniperZoneAction.Attack);
            RaiseLocalEvent(ref triggered);
        }
    }

    private void OnBeforeThrow(Entity<SniperZoneCheckComponent> ent, ref BeforeThrowEvent ev)
    {
        if (ev.Cancelled || !TryFindZoneCoord(ent, out var coord))
            return;

        ev.Cancelled = true;

        if (_net.IsServer)
        {
            var triggered = new SniperZoneTriggeredEvent(ent, coord, SniperZoneAction.Throw);
            RaiseLocalEvent(ref triggered);
        }
    }
}
