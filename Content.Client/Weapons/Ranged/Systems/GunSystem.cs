using System.Numerics;
using Content.Client.Animations;
using Content.Client.Gameplay;
using Content.Client.Items;
using Content.Client.Weapons.Ranged.Components;
using Content.Shared.Camera;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Shared.Animations;
using Robust.Shared.Audio;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using SharedGunSystem = Content.Shared.Weapons.Ranged.Systems.SharedGunSystem;
using TimedDespawnComponent = Robust.Shared.Spawners.TimedDespawnComponent;

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem : SharedGunSystem
{
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IInputManager _inputManager = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IStateManager _state = default!;
    [Dependency] private readonly AnimationPlayerSystem _animPlayer = default!;
    [Dependency] private readonly InputSystem _inputSystem = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public static readonly EntProtoId HitscanProto = "HitscanEffect";

    public bool SpreadOverlay
    {
        get => _spreadOverlay;
        set
        {
            if (_spreadOverlay == value)
                return;

            _spreadOverlay = value;

            if (_spreadOverlay)
            {
                _overlayManager.AddOverlay(new GunSpreadOverlay(
                    EntityManager,
                    _eyeManager,
                    Timing,
                    _inputManager,
                    _player,
                    this,
                    TransformSystem));
            }
            else
            {
                _overlayManager.RemoveOverlay<GunSpreadOverlay>();
            }
        }
    }

    private bool _spreadOverlay;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;
        SubscribeLocalEvent<AmmoCounterComponent, ItemStatusCollectMessage>(OnAmmoCounterCollect);
        SubscribeAllEvent<MuzzleFlashEvent>(OnMuzzleFlash);

        // Plays animated effects on the client.
        SubscribeNetworkEvent<HitscanEvent>(OnHitscan);

        InitializeMagazineVisuals();
        InitializeSpentAmmo();
    }

    private void OnMuzzleFlash(MuzzleFlashEvent args)
    {
        var gunUid = GetEntity(args.Uid);
        CreateEffect(gunUid, args, gunUid);
    }

    private void OnHitscan(HitscanEvent ev)
    {
        foreach (var a in ev.Sprites)
        {
            if (a.Sprite is not SpriteSpecifier.Rsi rsi)
                continue;

            var coords = GetCoordinates(a.coordinates);

            if (!TryComp(coords.EntityId, out TransformComponent? relativeXform))
                continue;

            var ent = Spawn(HitscanProto, coords);
            var sprite = Comp<SpriteComponent>(ent);

            var xform = Transform(ent);
            var targetWorldRot = a.angle + _xform.GetWorldRotation(relativeXform);
            var delta = targetWorldRot - _xform.GetWorldRotation(xform);
            _xform.SetLocalRotationNoLerp(ent, xform.LocalRotation + delta, xform);

            sprite[EffectLayers.Unshaded].AutoAnimated = false;
            _sprite.LayerSetSprite((ent, sprite), EffectLayers.Unshaded, rsi);
            _sprite.LayerSetRsiState((ent, sprite), EffectLayers.Unshaded, rsi.RsiState);
            _sprite.SetScale((ent, sprite), new Vector2(a.Distance, 1f));
            sprite[EffectLayers.Unshaded].Visible = true;

            var anim = new Animation()
            {
                Length = TimeSpan.FromSeconds(0.48f),
                AnimationTracks =
                {
                    new AnimationTrackSpriteFlick()
                    {
                        LayerKey = EffectLayers.Unshaded,
                        KeyFrames =
                        {
                            new AnimationTrackSpriteFlick.KeyFrame(rsi.RsiState, 0f),
                        }
                    }
                }
            };

            _animPlayer.Play(ent, anim, "hitscan-effect");
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!Timing.IsFirstTimePredicted)
            return;

        var entityNull = _player.LocalEntity;

        if (entityNull == null || !TryComp<CombatModeComponent>(entityNull, out var combat) || !combat.IsInCombatMode)
        {
            return;
        }

        var entity = entityNull.Value;

        if (!TryGetGun(entity, out var gunUid, out var gun))
        {
            return;
        }

        var useKey = gun.UseKey ? EngineKeyFunctions.Use : EngineKeyFunctions.UseSecondary;

        if (_inputSystem.CmdStates.GetState(useKey) != BoundKeyState.Down && !gun.BurstActivated)
        {
            if (gun.ShotCounter != 0)
                RaisePredictiveEvent(new RequestStopShootEvent { Gun = GetNetEntity(gunUid) });
            return;
        }

        if (gun.NextFire > Timing.CurTime)
            return;

        var mousePos = _eyeManager.PixelToMap(_inputManager.MouseScreenPosition);

        if (mousePos.MapId == MapId.Nullspace)
        {
            if (gun.ShotCounter != 0)
                RaisePredictiveEvent(new RequestStopShootEvent { Gun = GetNetEntity(gunUid) });

            return;
        }

        var coordinates = TransformSystem.ToCoordinates(entity, mousePos);

        NetEntity? target = null;
        if (_state.CurrentState is GameplayStateBase screen)
            target = GetNetEntity(screen.GetClickedEntity(mousePos));

        Log.Debug($"Sending shoot request tick {Timing.CurTick} / {Timing.CurTime}");

        RaisePredictiveEvent(new RequestShootEvent
        {
            Target = target,
            Coordinates = GetNetCoordinates(coordinates),
            Gun = GetNetEntity(gunUid),
        });
    }

    public override void ShootProjectile(EntityUid uid, Vector2 direction, Vector2 gunVelocity, EntityUid? gunUid, EntityUid? user = null, float speed = 20f)
    {
        // Tag client-side predicted bullets. PredictedProjectileSystem.OnClientComponentStartup
        // enables physics prediction automatically when this component is added.
        if (IsClientSide(uid) && Timing.IsFirstTimePredicted)
            EnsureComp<PredictedProjectileClientComponent>(uid);

        base.ShootProjectile(uid, direction, gunVelocity, gunUid, user, speed);
    }

    public override void Shoot(
        EntityUid gunUid,
        GunComponent gun,
        List<(EntityUid? Entity, IShootable Shootable)> ammo,
        EntityCoordinates fromCoordinates,
        EntityCoordinates toCoordinates,
        out bool userImpulse,
        EntityUid? user = null,
        bool throwItems = false)
    {
        userImpulse = true;

        var direction = TransformSystem.ToMapCoordinates(fromCoordinates).Position - TransformSystem.ToMapCoordinates(toCoordinates).Position;
        var worldAngle = direction.ToAngle().Opposite();

        var shouldPredict = Timing.IsFirstTimePredicted && user == _player.LocalEntity;

        // Compute a grid-parented spawn position matching what the server uses, plus the gun velocity.
        var fromEnt = fromCoordinates;
        var gunVelocity = Vector2.Zero;
        if (shouldPredict && direction != Vector2.Zero)
        {
            var fromMap = TransformSystem.ToMapCoordinates(fromCoordinates);
            fromEnt = MapManager.TryFindGridAt(fromMap, out var gridUid, out _)
                ? TransformSystem.WithEntityId(fromCoordinates, gridUid)
                : new EntityCoordinates(_maps.GetMapOrInvalid(fromMap.MapId), fromMap.Position);
            gunVelocity = Physics.GetMapLinearVelocity(gunUid);
        }

        foreach (var (ent, shootable) in ammo)
        {
            if (throwItems)
            {
                Recoil(user, direction, gun.CameraRecoilScalarModified);
                if (IsClientSide(ent!.Value))
                    Del(ent.Value);
                else
                    RemoveShootable(ent.Value);
                continue;
            }

            switch (shootable)
            {
                case CartridgeAmmoComponent cartridge:
                    if (!cartridge.Spent)
                    {
                        SetCartridgeSpent(ent!.Value, cartridge, true);
                        MuzzleFlash(gunUid, cartridge, worldAngle, user);
                        Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
                        Recoil(user, direction, gun.CameraRecoilScalarModified);

                        if (shouldPredict && direction != Vector2.Zero)
                        {
                            // Call GetRecoilAngle for its side-effect of updating CurrentAngle/LastFire
                            // so gun state stays in sync with the server. Do NOT use its return value:
                            // the client and server RNG seeds differ, so the recoiled angle would be wrong
                            // and the predicted bullet would visually fly past the target.
                            GetRecoilAngle(Timing.CurTime, gun, worldAngle);
                            var predicted = Spawn(cartridge.Prototype, fromEnt);
                            ShootProjectile(predicted, worldAngle.ToVec(), gunVelocity, gunUid, user, gun.ProjectileSpeedModified);
                        }
                    }
                    else
                    {
                        userImpulse = false;
                        Audio.PlayPredicted(gun.SoundEmpty, gunUid, user);
                    }

                    if (IsClientSide(ent!.Value))
                        Del(ent.Value);
                    break;

                case AmmoComponent newAmmo:
                    MuzzleFlash(gunUid, newAmmo, worldAngle, user);
                    Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
                    Recoil(user, direction, gun.CameraRecoilScalarModified);

                    if (IsClientSide(ent!.Value))
                        Del(ent.Value);
                    else
                        RemoveShootable(ent.Value);
                    break;

                case HitscanAmmoComponent:
                    Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, user);
                    Recoil(user, direction, gun.CameraRecoilScalarModified);
                    break;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Existing helpers (unchanged from original)
    // -------------------------------------------------------------------------

    private void Recoil(EntityUid? user, Vector2 recoil, float recoilScalar)
    {
        if (!Timing.IsFirstTimePredicted || user == null || recoil == Vector2.Zero || recoilScalar == 0)
            return;

        _recoil.KickCamera(user.Value, recoil.Normalized() * 0.5f * recoilScalar);
    }

    protected override void Popup(string message, EntityUid? uid, EntityUid? user)
    {
        if (uid == null || user == null || !Timing.IsFirstTimePredicted)
            return;

        PopupSystem.PopupEntity(message, uid.Value, user.Value);
    }

    protected override void CreateEffect(EntityUid gunUid, MuzzleFlashEvent message, EntityUid? tracked = null)
    {
        if (!Timing.IsFirstTimePredicted)
            return;

        if (gunUid == EntityUid.Invalid)
        {
            Log.Debug($"Invalid Entity sent MuzzleFlashEvent (proto: {message.Prototype}, gun: {ToPrettyString(gunUid)})");
            return;
        }

        var gunXform = Transform(gunUid);
        var gridUid = gunXform.GridUid;
        EntityCoordinates coordinates;

        if (TryComp(gridUid, out MapGridComponent? mapGrid))
        {
            coordinates = new EntityCoordinates(gridUid.Value, _maps.LocalToGrid(gridUid.Value, mapGrid, gunXform.Coordinates));
        }
        else if (gunXform.MapUid != null)
        {
            coordinates = new EntityCoordinates(gunXform.MapUid.Value, TransformSystem.GetWorldPosition(gunXform));
        }
        else
        {
            return;
        }

        var ent = Spawn(message.Prototype, coordinates);
        TransformSystem.SetWorldRotationNoLerp(ent, message.Angle);

        if (tracked != null)
        {
            var track = EnsureComp<TrackUserComponent>(ent);
            track.User = tracked;
            track.Offset = Vector2.UnitX / 2f;
        }

        var lifetime = 0.4f;

        if (TryComp<TimedDespawnComponent>(gunUid, out var despawn))
        {
            lifetime = despawn.Lifetime;
        }

        var anim = new Animation()
        {
            Length = TimeSpan.FromSeconds(lifetime),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(1f), 0),
                        new AnimationTrackProperty.KeyFrame(Color.White.WithAlpha(0f), lifetime)
                    }
                }
            }
        };

        _animPlayer.Play(ent, anim, "muzzle-flash");
        if (!TryComp(gunUid, out PointLightComponent? light))
        {
            light = Factory.GetComponent<PointLightComponent>();
            light.NetSyncEnabled = false;
            AddComp(gunUid, light);
        }

        Lights.SetEnabled(gunUid, true, light);
        Lights.SetRadius(gunUid, 2f, light);
        Lights.SetColor(gunUid, Color.FromHex("#cc8e2b"), light);
        Lights.SetEnergy(gunUid, 5f, light);

        var animTwo = new Animation()
        {
            Length = TimeSpan.FromSeconds(lifetime),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(PointLightComponent),
                    Property = nameof(PointLightComponent.Energy),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(5f, 0),
                        new AnimationTrackProperty.KeyFrame(0f, lifetime)
                    }
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(PointLightComponent),
                    Property = nameof(PointLightComponent.AnimatedEnable),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(true, 0),
                        new AnimationTrackProperty.KeyFrame(false, lifetime)
                    }
                }
            }
        };

        var uidPlayer = EnsureComp<AnimationPlayerComponent>(gunUid);

        _animPlayer.Stop(gunUid, uidPlayer, "muzzle-flash-light");
        _animPlayer.Play((gunUid, uidPlayer), animTwo, "muzzle-flash-light");
    }

    // TODO: Move RangedDamageSoundComponent to shared so this can be predicted.
    public override void PlayImpactSound(EntityUid otherEntity, DamageSpecifier? modifiedDamage, SoundSpecifier? weaponSound, bool forceWeaponSound) {}
}
