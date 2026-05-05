using Content.Shared.Effects;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Physics;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Client.Projectiles;

/// <summary>
/// Owns all client-side behaviour for predicted projectiles spawned by the local player.
///
/// On hit the system:
///   - Hides the bullet sprite at the contact point immediately.
///   - Spawns the impact VFX at the exact ray-contact position so it doesn't appear past the target.
///   - Flashes the hit entity red immediately via the shared ColorFlashEffectSystem.
///   - Increments a pending-VFX counter so ProjectileSystem.OnProjectileImpact can suppress
///     the server's duplicate ImpactEffectEvent (which arrives after RTT/2).
///   - ColorFlashEffectSystem suppresses the server's duplicate ColorFlashEffectEvent because it
///     skips any entity whose animation is already running.
/// </summary>
public sealed class PredictedProjectileSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly AnimationPlayerSystem _animPlayer = default!;
    [Dependency] private readonly SharedColorFlashEffectSystem _colorFlash = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private const int BulletRayMask = (int)(CollisionGroup.BulletImpassable | CollisionGroup.Impassable);

    // Timestamps of locally-predicted impact VFX that haven't been matched to a server
    // ImpactEffectEvent yet. ProjectileSystem.OnProjectileImpact consumes one entry when
    // the server event arrives so it doesn't spawn a duplicate particle.
    private readonly List<TimeSpan> _pendingVFX = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PredictedProjectileClientComponent, ComponentStartup>(OnClientComponentStartup);
        SubscribeLocalEvent<PredictedProjectileClientComponent, UpdateIsPredictedEvent>(OnUpdateIsPredicted);
        SubscribeLocalEvent<PhysicsUpdateBeforeSolveEvent>(OnBeforeSolve);
        SubscribeLocalEvent<PhysicsUpdateAfterSolveEvent>(OnAfterSolve);

        SubscribeLocalEvent<PredictedProjectileServerComponent, ComponentStartup>(OnServerProjectileStartup);
    }

    // -------------------------------------------------------------------------
    // Public API — used by ProjectileSystem to suppress duplicate server VFX
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true and removes one pending entry if a locally-predicted VFX was recently
    /// spawned that hasn't been matched to a server ImpactEffectEvent yet.
    /// Call this at the top of OnProjectileImpact to skip the duplicate.
    /// </summary>
    public bool ConsumePendingVFX()
    {
        var now = _timing.CurTime;
        _pendingVFX.RemoveAll(t => (now - t).TotalSeconds > 1.0);
        if (_pendingVFX.Count == 0)
            return false;
        _pendingVFX.RemoveAt(0);
        return true;
    }

    // -------------------------------------------------------------------------
    // Client bullet — physics prediction enablement
    // -------------------------------------------------------------------------

    private void OnClientComponentStartup(EntityUid uid, PredictedProjectileClientComponent _, ref ComponentStartup args)
    {
        _physics.UpdateIsPredicted(uid);
    }

    private void OnUpdateIsPredicted(EntityUid uid, PredictedProjectileClientComponent _, ref UpdateIsPredictedEvent args)
    {
        args.IsPredicted = true;
    }

    // -------------------------------------------------------------------------
    // Server bullet — hide duplicate
    // -------------------------------------------------------------------------

    private void OnServerProjectileStartup(Entity<PredictedProjectileServerComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.ClientEnt != _player.LocalEntity)
            return;

        if (TryComp<SpriteComponent>(ent, out var sprite))
            _sprite.SetVisible((ent, sprite), false);
    }

    // -------------------------------------------------------------------------
    // Client bullet — position save / hit detection / resimulation restore
    // -------------------------------------------------------------------------

    private void OnBeforeSolve(ref PhysicsUpdateBeforeSolveEvent ev)
    {
        if (!ev.Prediction)
            return;

        var query = EntityQueryEnumerator<PredictedProjectileClientComponent, TransformComponent>();
        while (query.MoveNext(out _, out var predicted, out var xform))
            predicted.Coordinates = xform.Coordinates;
    }

    private void OnAfterSolve(ref PhysicsUpdateAfterSolveEvent ev)
    {
        if (!ev.Prediction)
            return;

        if (_timing.IsFirstTimePredicted)
        {
            CheckSegmentHits();
            return;
        }

        var query = EntityQueryEnumerator<PredictedProjectileClientComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var predicted, out _))
        {
            if (predicted.Coordinates is { } coords)
                _transform.SetCoordinates(uid, coords);
            predicted.Coordinates = null;
        }
    }

    private void CheckSegmentHits()
    {
        var query = EntityQueryEnumerator<PredictedProjectileClientComponent, TransformComponent, ProjectileComponent>();
        while (query.MoveNext(out var uid, out var predicted, out var xform, out var proj))
        {
            if (TerminatingOrDeleted(uid))
                continue;

            if (predicted.Coordinates is not { } savedCoords)
                continue;

            var prevMap = _transform.ToMapCoordinates(savedCoords);
            if (prevMap.MapId != xform.MapID)
                continue;

            var prevPos  = prevMap.Position;
            var currPos  = _transform.GetWorldPosition(xform);
            var delta    = currPos - prevPos;

            if (delta.LengthSquared() < 0.0001f)
                continue;

            var dist      = delta.Length();
            var direction = delta / dist;
            var ray       = new CollisionRay(prevPos, direction, BulletRayMask);
            var results   = _physics.IntersectRay(prevMap.MapId, ray, dist + 0.05f, uid, returnOnFirstHit: false);

            var closestDist   = float.MaxValue;
            var closestEntity = EntityUid.Invalid;

            foreach (var result in results)
            {
                if (proj.IgnoreShooter && (result.HitEntity == proj.Shooter || result.HitEntity == proj.Weapon))
                    continue;

                if (result.Distance < closestDist)
                {
                    closestDist   = result.Distance;
                    closestEntity = result.HitEntity;
                }
            }

            if (!closestEntity.IsValid())
                continue;

            // Exact contact point in world space.
            var hitWorldPos = prevPos + direction * closestDist;
            var hitCoords   = _transform.ToCoordinates(new MapCoordinates(hitWorldPos, prevMap.MapId));

            // Spawn impact VFX immediately at the contact point.
            // Register a pending entry so ProjectileSystem can suppress the server duplicate.
            if (proj.ImpactEffect is { } effect)
            {
                SpawnImpactEffect(effect, hitCoords);
                _pendingVFX.Add(_timing.CurTime);
            }

            // Flash the hit entity red immediately.
            // ColorFlashEffectSystem will skip the server's duplicate because the
            // animation is already running when the server event arrives.
            _colorFlash.RaiseEffect(Color.Red, new List<EntityUid> { closestEntity }, Filter.Empty());

            if (TryComp<SpriteComponent>(uid, out var sprite))
                _sprite.SetVisible((uid, sprite), false);

            QueueDel(uid);
        }
    }

    private void SpawnImpactEffect(EntProtoId prototype, EntityCoordinates coords)
    {
        var ent = Spawn(prototype, coords);
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        sprite[EffectLayers.Unshaded].AutoAnimated = false;
        _sprite.LayerMapTryGet((ent, sprite), EffectLayers.Unshaded, out var layer, false);
        var state    = _sprite.LayerGetRsiState((ent, sprite), layer);
        var lifetime = 0.5f;

        if (TryComp<TimedDespawnComponent>(ent, out var despawn))
            lifetime = despawn.Lifetime;

        var anim = new Animation
        {
            Length = TimeSpan.FromSeconds(lifetime),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick
                {
                    LayerKey = EffectLayers.Unshaded,
                    KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(state.Name, 0f) }
                }
            }
        };

        _animPlayer.Play(ent, anim, "impact-effect");
    }

    // -------------------------------------------------------------------------
    // Client bullet — lerp suppression
    // -------------------------------------------------------------------------

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<PredictedProjectileClientComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            xform.ActivelyLerping = false;
            if (xform.NextPosition is { } pos)
                _transform.SetLocalPositionNoLerp(uid, pos, xform);
            if (xform.NextRotation is { } rot)
                _transform.SetLocalRotationNoLerp(uid, rot, xform);
        }
    }
}
