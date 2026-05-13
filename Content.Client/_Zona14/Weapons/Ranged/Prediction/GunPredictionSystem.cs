// SPDX-License-Identifier: MIT
// Ported from RMC-14 Content.Client/_RMC14/Weapons/Ranged/Prediction/GunPredictionSystem.cs@2f5dc02e44.
//
// Zona14: matches RMC's structure — on a predicted-twin StartCollideEvent, raise the predicted-hit
// network event and call SharedProjectileSystem.ProjectileCollide, which runs the same reflect/hit
// event flow as the server and deletes the client-side twin (IsClientSide branch). The collision
// guards (fixture, hardness, spent, OnlyCollideWhenShot) live in SharedProjectileSystem.OnStartCollide
// so server and client share a single entry point.
using Content.Client.Projectiles;
using Content.Shared._Zona14.Weapons.Ranged.Prediction;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Client.GameObjects;
using Robust.Client.Physics;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Client._Zona14.Weapons.Ranged.Prediction;

public sealed class GunPredictionSystem : SharedGunPredictionSystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly ProjectileSystem _projectile = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<IgnorePredictionHideComponent> _ignorePredictionHideQuery;
    private EntityQuery<IgnorePredictionHitComponent> _ignorePredictionHitQuery;
    private EntityQuery<SpriteComponent> _spriteQuery;

    public override void Initialize()
    {
        base.Initialize();

        _ignorePredictionHideQuery = GetEntityQuery<IgnorePredictionHideComponent>();
        _ignorePredictionHitQuery = GetEntityQuery<IgnorePredictionHitComponent>();
        _spriteQuery = GetEntityQuery<SpriteComponent>();

        SubscribeLocalEvent<PhysicsUpdateBeforeSolveEvent>(OnBeforeSolve);
        SubscribeLocalEvent<PhysicsUpdateAfterSolveEvent>(OnAfterSolve);
        SubscribeLocalEvent<RequestShootEvent>(OnShootRequest);

        SubscribeLocalEvent<PredictedProjectileClientComponent, UpdateIsPredictedEvent>(OnClientProjectileUpdateIsPredicted);
        SubscribeLocalEvent<PredictedProjectileClientComponent, StartCollideEvent>(OnClientProjectileStartCollide);

        SubscribeLocalEvent<PredictedProjectileServerComponent, ComponentStartup>(OnServerProjectileStartup);

        UpdatesBefore.Add(typeof(TransformSystem));
    }

    private void OnBeforeSolve(ref PhysicsUpdateBeforeSolveEvent ev)
    {
        var query = EntityQueryEnumerator<PredictedProjectileClientComponent>();
        while (query.MoveNext(out var uid, out var predicted))
        {
            predicted.Coordinates = Transform(uid).Coordinates;
        }
    }

    private void OnAfterSolve(ref PhysicsUpdateAfterSolveEvent ev)
    {
        if (_timing.IsFirstTimePredicted)
            return;
        var query = EntityQueryEnumerator<PredictedProjectileClientComponent>();
        while (query.MoveNext(out var uid, out var predicted))
        {
            if (predicted.Coordinates is { } coordinates)
                _transform.SetCoordinates(uid, coordinates);

            predicted.Coordinates = null;
        }
    }

    private void OnShootRequest(RequestShootEvent ev, EntitySessionEventArgs args)
    {
        ShootRequested(ev.Gun, ev.Coordinates, ev.Target, null, args.SenderSession);
    }

    private void OnClientProjectileUpdateIsPredicted(Entity<PredictedProjectileClientComponent> ent, ref UpdateIsPredictedEvent args)
    {
        args.IsPredicted = true;
    }

    private void OnClientProjectileStartCollide(Entity<PredictedProjectileClientComponent> ent, ref StartCollideEvent args)
    {
        if (!TryComp(ent, out ProjectileComponent? projectile) ||
            !TryComp(ent, out PhysicsComponent? physics) ||
            _ignorePredictionHitQuery.HasComp(args.OtherEntity))
        {
            return;
        }

        // Mirror SharedProjectileSystem.OnStartCollide's guards. RMC's client GunPredictionSystem
        // omits these because RMC subscribes a single shared OnStartCollide that runs first; we
        // can't rely on ordering across two independent subscriptions, and any soft-fixture or
        // wrong-fixture StartCollideEvent that bypasses the shared guards would otherwise reach
        // ProjectileCollide → QueueDel and the shooter never sees their own bullet.
        if (args.OurFixtureId != SharedProjectileSystem.ProjectileFixture
            || !args.OtherFixture.Hard
            || projectile.ProjectileSpent
            || projectile is { Weapon: null, OnlyCollideWhenShot: true })
        {
            return;
        }

        // Notify the server so it can apply server-validated damage (stalker armor / penetration),
        // then run the same ProjectileCollide cleanup as the shared OnStartCollide. The ProjectileSpent
        // early-return inside ProjectileCollide makes the duplicate call a no-op.
        var netEnt = GetNetEntity(args.OtherEntity);
        var pos = _transform.GetMapCoordinates(args.OtherEntity);
        var hit = new HashSet<(NetEntity, MapCoordinates)> { (netEnt, pos) };
        var ev = new PredictedProjectileHitEvent(ent.Owner.Id, hit);
        RaiseNetworkEvent(ev);

        _projectile.ProjectileCollide((ent, projectile, physics), args.OtherEntity);
    }

    private void OnServerProjectileStartup(Entity<PredictedProjectileServerComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.ClientEnt != _player.LocalEntity)
            return;

        if (_ignorePredictionHideQuery.HasComp(ent))
            return;

        if (_spriteQuery.TryComp(ent, out var sprite))
            _sprite.SetVisible((ent, sprite), false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        // Zona14: RMC's TODO-fallback contact-poll using GetContactingEntities(approximate: true)
        // is dropped. `approximate: true` returns AABB-overlap-only contacts (see
        // RobustToolbox/Robust.Shared/Physics/Systems/SharedPhysicsSystem.Queries.cs:220), so the
        // poll fires false positives for entities the bullet is merely *near*, deleting the twin
        // a tile or two into its flight. The shared SharedProjectileSystem.OnStartCollide handler
        // is the single authoritative entry point for predicted hits.

        var predictedQuery = EntityQueryEnumerator<PredictedProjectileHitComponent, SpriteComponent, TransformComponent>();
        while (predictedQuery.MoveNext(out var hit, out var sprite, out var xform))
        {
            var origin = hit.Origin;
            var coordinates = xform.Coordinates;
            if (!origin.TryDistance(EntityManager, _transform, coordinates, out var distance) ||
                distance >= hit.Distance)
            {
                sprite.Visible = false;
            }
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // TODO bullet prediction remove this when lerping doesnt make the client's entity slightly slower
        var projectiles = EntityQueryEnumerator<PredictedProjectileClientComponent, TransformComponent>();
        while (projectiles.MoveNext(out _, out var xform))
        {
            xform.ActivelyLerping = false;
        }
    }
}
