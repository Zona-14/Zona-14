using System.Numerics;
using Content.Server.Cargo.Systems;
using Content.Server.Movement.Systems;
using Content.Server.Projectiles.Components;
using Content.Server.Weapons.Ranged.Components;
using Content.Shared.Cargo;
using Content.Shared._DZ.FarGunshot; // Stalker-using
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Weapons.Ranged.Systems;

public sealed partial class GunSystem : SharedGunSystem
{
    [Dependency] private readonly PricingSystem _pricing = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly LagCompensationSystem _lagComp = default!;

    private const float DamagePitchVariation = 0.05f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BallisticAmmoProviderComponent, PriceCalculationEvent>(OnBallisticPrice);
    }

    private void OnBallisticPrice(EntityUid uid, BallisticAmmoProviderComponent component, ref PriceCalculationEvent args)
    {
        if (string.IsNullOrEmpty(component.Proto) || component.UnspawnedCount == 0)
            return;

        if (!ProtoManager.TryIndex<EntityPrototype>(component.Proto, out var proto))
        {
            Log.Error($"Unable to find fill prototype for price on {component.Proto} on {ToPrettyString(uid)}");
            return;
        }

        // Probably good enough for most.
        var price = _pricing.GetEstimatedPrice(proto);
        args.Price += price * component.UnspawnedCount;
    }

    public override void Shoot(EntityUid gunUid, GunComponent gun, List<(EntityUid? Entity, IShootable Shootable)> ammo,
        EntityCoordinates fromCoordinates, EntityCoordinates toCoordinates, out bool userImpulse, EntityUid? user = null, bool throwItems = false)
    {
        userImpulse = true;

        if (user != null)
        {
            var selfEvent = new SelfBeforeGunShotEvent(user.Value, (gunUid, gun), ammo);
            RaiseLocalEvent(user.Value, selfEvent);
            if (selfEvent.Cancelled)
            {
                userImpulse = false;
                return;
            }
        }

        var fromMap = TransformSystem.ToMapCoordinates(fromCoordinates);
        var toMap = TransformSystem.ToMapCoordinates(toCoordinates).Position;
        var mapDirection = toMap - fromMap.Position;
        var mapAngle = mapDirection.ToAngle();
        var angle = GetRecoilAngle(Timing.CurTime, gun, mapDirection.ToAngle());

        // If applicable, this ensures the projectile is parented to grid on spawn, instead of the map.
        var fromEnt = MapManager.TryFindGridAt(fromMap, out var gridUid, out _)
            ? TransformSystem.WithEntityId(fromCoordinates, gridUid)
            : new EntityCoordinates(_map.GetMapOrInvalid(fromMap.MapId), fromMap.Position);

        // Update shot based on the recoil
        toMap = fromMap.Position + angle.ToVec() * mapDirection.Length();
        mapDirection = toMap - fromMap.Position;
        var gunVelocity = Physics.GetMapLinearVelocity(fromEnt);

        // I must be high because this was getting tripped even when true.
        // DebugTools.Assert(direction != Vector2.Zero);
        var shotProjectiles = new List<EntityUid>(ammo.Count);

        foreach (var (ent, shootable) in ammo)
        {
            // pneumatic cannon doesn't shoot bullets it just throws them, ignore ammo handling
            if (throwItems && ent != null)
            {
                ShootOrThrow(ent.Value, mapDirection, gunVelocity, gun, gunUid, user);
                continue;
            }

            // TODO: Clean this up in a gun refactor at some point - too much copy pasting
            switch (shootable)
            {
                // Cartridge shoots something else
                case CartridgeAmmoComponent cartridge:
                    if (!cartridge.Spent)
                    {
                        var uid = Spawn(cartridge.Prototype, fromEnt);
                        CreateAndFireProjectiles(uid, cartridge);

                        // stalker-changes-start
                        var farSoundEvent = new FargunshotEvent(gunUid.Id);
                        RaiseLocalEvent(gunUid, farSoundEvent);
                        // stalker-changes-end

                        RaiseLocalEvent(ent!.Value, new AmmoShotEvent()
                        {
                            FiredProjectiles = shotProjectiles,
                        });

                        SetCartridgeSpent(ent.Value, cartridge, true);

                        if (cartridge.DeleteOnSpawn)
                            Del(ent.Value);
                    }
                    else
                    {
                        userImpulse = false;
                        Audio.PlayPredicted(gun.SoundEmpty, gunUid, user);
                    }

                    // Something like ballistic might want to leave it in the container still
                    if (!cartridge.DeleteOnSpawn && !Containers.IsEntityInContainer(ent!.Value))
                        EjectCartridge(ent.Value, angle);

                    Dirty(ent!.Value, cartridge);
                    break;
                // Ammo shoots itself
                case AmmoComponent newAmmo:
                    if (ent == null)
                        break;
                    CreateAndFireProjectiles(ent.Value, newAmmo);

                    break;
                case HitscanAmmoComponent:
                    if (ent == null)
                        break;

                    var hitscanEv = new HitscanTraceEvent
                    {
                        FromCoordinates = fromCoordinates,
                        ShotDirection = mapDirection.Normalized(),
                        Gun = gunUid,
                        Shooter = user,
                        Target = gun.Target,
                    };
                    RaiseLocalEvent(ent.Value, ref hitscanEv);

                    Del(ent);

                    Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        RaiseLocalEvent(gunUid, new AmmoShotEvent()
        {
            FiredProjectiles = shotProjectiles,
        });

        void CreateAndFireProjectiles(EntityUid ammoEnt, AmmoComponent ammoComp)
        {
            if (TryComp<ProjectileSpreadComponent>(ammoEnt, out var ammoSpreadComp))
            {
                var spreadEvent = new GunGetAmmoSpreadEvent(ammoSpreadComp.Spread);
                RaiseLocalEvent(gunUid, ref spreadEvent);

                var angles = LinearSpread(mapAngle - spreadEvent.Spread / 2,
                    mapAngle + spreadEvent.Spread / 2, ammoSpreadComp.Count);

                ShootOrThrow(ammoEnt, angles[0].ToVec(), gunVelocity, gun, gunUid, user);
                shotProjectiles.Add(ammoEnt);

                for (var i = 1; i < ammoSpreadComp.Count; i++)
                {
                    var newuid = Spawn(ammoSpreadComp.Proto, fromEnt);
                    ShootOrThrow(newuid, angles[i].ToVec(), gunVelocity, gun, gunUid, user);
                    shotProjectiles.Add(newuid);
                }
            }
            else
            {
                ShootOrThrow(ammoEnt, mapDirection, gunVelocity, gun, gunUid, user);
                shotProjectiles.Add(ammoEnt);
            }

            MuzzleFlash(gunUid, ammoComp, mapDirection.ToAngle(), user);
            Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
        }
    }

    private void ShootOrThrow(EntityUid uid, Vector2 mapDirection, Vector2 gunVelocity, GunComponent gun, EntityUid gunUid, EntityUid? user)
    {
        if (gun.Target is { } target && !TerminatingOrDeleted(target))
        {
            var targeted = EnsureComp<TargetedProjectileComponent>(uid);
            targeted.Target = target;
            Dirty(uid, targeted);
        }

        // Do a throw
        if (!HasComp<ProjectileComponent>(uid))
        {
            RemoveShootable(uid);
            // TODO: Someone can probably yeet this a billion miles so need to pre-validate input somewhere up the call stack.
            ThrowingSystem.TryThrow(uid, mapDirection, gun.ProjectileSpeedModified, user);
            return;
        }

        ShootProjectile(uid, mapDirection, gunVelocity, gunUid, user, gun.ProjectileSpeedModified);
    }

    /// <summary>
    /// Gets a linear spread of angles between start and end.
    /// </summary>
    /// <param name="start">Start angle in degrees</param>
    /// <param name="end">End angle in degrees</param>
    /// <param name="intervals">How many shots there are</param>
    private Angle[] LinearSpread(Angle start, Angle end, int intervals)
    {
        var angles = new Angle[intervals];
        DebugTools.Assert(intervals > 1);

        for (var i = 0; i <= intervals - 1; i++)
        {
            angles[i] = new Angle(start + (end - start) * i / (intervals - 1));
        }

        return angles;
    }

    public override void ShootProjectile(EntityUid uid, System.Numerics.Vector2 direction, System.Numerics.Vector2 gunVelocity, EntityUid? gunUid, EntityUid? user = null, float speed = 20f)
    {
        if (user != null)
        {
            var predicted = EnsureComp<PredictedProjectileServerComponent>(uid);
            predicted.ClientEnt = user;

            // Store fire metadata and run a lag-compensated target search so we can
            // recover hits that the physics simulation misses due to target movement.
            if (TryComp<ActorComponent>(user.Value, out var actor))
            {
                var xform = Transform(uid);
                var mapPos = TransformSystem.ToMapCoordinates(xform.Coordinates);
                var dir = direction.Normalized();

                var fired = EnsureComp<ProjectileFiredComponent>(uid);
                fired.FiredBy           = actor.PlayerSession;
                fired.FiredAt           = Timing.CurTime;
                fired.FiredFromWorldPos = mapPos.Position;
                fired.FiredDirection    = dir;
                fired.Speed             = speed;

                if (FindLagCompTarget(actor.PlayerSession, mapPos, dir, user.Value, out var lagTarget, out var lagTargetPos))
                {
                    fired.LagCompTarget         = lagTarget;
                    fired.LagCompTargetWorldPos = lagTargetPos;
                }
            }
        }

        base.ShootProjectile(uid, direction, gunVelocity, gunUid, user, speed);
    }

    /// <summary>
    /// Iterates entities with <see cref="Content.Server.Movement.Components.LagCompensationComponent"/> and returns the
    /// closest one that lies within <paramref name="hitRadius"/> of the firing ray at the time the client fired.
    /// </summary>
    private bool FindLagCompTarget(
        ICommonSession session,
        MapCoordinates fireMapPos,
        Vector2 direction,
        EntityUid shooter,
        out EntityUid target,
        out Vector2 targetWorldPos)
    {
        const float HitRadius = 0.5f;
        const float MaxRange  = 100f;

        target         = default;
        targetWorldPos = default;

        var closestDot = float.MaxValue;
        var found      = false;

        var query = AllEntityQuery<Content.Server.Movement.Components.LagCompensationComponent, TransformComponent>();
        while (query.MoveNext(out var candidateUid, out _, out var candidateXform))
        {
            if (candidateUid == shooter)
                continue;

            if (candidateXform.MapID != fireMapPos.MapId)
                continue;

            var (lagCoords, _) = _lagComp.GetCoordinatesAngle(candidateUid, session, candidateXform);
            if (lagCoords == EntityCoordinates.Invalid)
                continue;

            var lagWorldPos = TransformSystem.ToMapCoordinates(lagCoords).Position;
            var toEntity    = lagWorldPos - fireMapPos.Position;
            var dot         = Vector2.Dot(toEntity, direction);

            if (dot <= 0f || dot > MaxRange)
                continue;

            var closestPoint = fireMapPos.Position + direction * dot;
            var perpDist     = (lagWorldPos - closestPoint).Length();

            if (perpDist <= HitRadius && dot < closestDot)
            {
                closestDot     = dot;
                target         = candidateUid;
                targetWorldPos = lagWorldPos;
                found          = true;
            }
        }

        return found;
    }

    protected override void Popup(string message, EntityUid? uid, EntityUid? user) { }

    protected override void CreateEffect(EntityUid gunUid, MuzzleFlashEvent message, EntityUid? user = null)
    {
        var filter = Filter.Pvs(gunUid, entityManager: EntityManager);

        if (TryComp<ActorComponent>(user, out var actor))
            filter.RemovePlayer(actor.PlayerSession);

        RaiseNetworkEvent(message, filter);
    }

    public override void PlayImpactSound(EntityUid otherEntity, DamageSpecifier? modifiedDamage, SoundSpecifier? weaponSound, bool forceWeaponSound)
    {
        DebugTools.Assert(!Deleted(otherEntity), "Impact sound entity was deleted");

        // Like projectiles and melee,
        // 1. Entity specific sound
        // 2. Ammo's sound
        // 3. Nothing
        var playedSound = false;

        if (!forceWeaponSound && modifiedDamage != null && modifiedDamage.GetTotal() > 0 && TryComp<RangedDamageSoundComponent>(otherEntity, out var rangedSound))
        {
            var type = SharedMeleeWeaponSystem.GetHighestDamageSound(modifiedDamage, ProtoManager);

            if (type != null && rangedSound.SoundTypes?.TryGetValue(type, out var damageSoundType) == true)
            {
                Audio.PlayPvs(damageSoundType, otherEntity, AudioParams.Default.WithVariation(DamagePitchVariation));
                playedSound = true;
            }
            else if (type != null && rangedSound.SoundGroups?.TryGetValue(type, out var damageSoundGroup) == true)
            {
                Audio.PlayPvs(damageSoundGroup, otherEntity, AudioParams.Default.WithVariation(DamagePitchVariation));
                playedSound = true;
            }
        }

        if (!playedSound && weaponSound != null)
        {
            Audio.PlayPvs(weaponSound, otherEntity);
        }
    }
}
