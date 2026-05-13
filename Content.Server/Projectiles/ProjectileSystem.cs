using Content.Server.Administration.Logs;
using Content.Server.Destructible;
using Content.Server.Effects;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Armor;
using Content.Shared.Camera;
using Content.Shared.Inventory;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Projectiles;

public sealed class ProjectileSystem : SharedProjectileSystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly ColorFlashEffectSystem _color = default!;
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly DestructibleSystem _destructibleSystem = default!;
    [Dependency] private readonly GunSystem _guns = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _sharedCameraRecoil = default!;
    [Dependency] private readonly InventorySystem _inventory = default!; // Stalker-Changes
    [Dependency] private readonly IPrototypeManager _prototype = default!; // Stalker-Changes

    // Zona14: OnStartCollide subscription and the OnStartCollide guards now live in
    //         SharedProjectileSystem so the client-side predicted twin routes through the same
    //         entry point. ProjectileCollide is an override of the shared method.

    // Zona14: server override of SharedProjectileSystem.ProjectileCollide. Preserves the
    //         stalker-fork armor / ignoreResistors / penetration logic byte-for-byte. When
    //         `predicted` is true, the firer's client has already drawn the hit effect / shake
    //         / impact broadcast, so we skip those to avoid double-rendering.
    public override void ProjectileCollide(Entity<ProjectileComponent, PhysicsComponent> projectile,
        EntityUid target, bool predicted = false)
    {
        var (uid, component, ourBody) = projectile;

        // stalker-changes-start
        var ignoreResitance = false;
        List<EntityUid> ignore = new();
        string[] slots = {
            "outerClothing",
            "head",
            "cloak",
            "eyes",
            "ears",
            "mask",
            "jumpsuit",
            "neck",
            "back",
            "belt",
            "gloves",
            "shoes",
            "id",
            "legs",
            "torso"
        };

        foreach (var slot in slots)
        {
            if (_inventory.TryGetSlotEntity(target, slot, out var entity) && TryComp<ArmorComponent>(entity, out var armorComp) && armorComp.ArmorClass.HasValue)
                if (component.ProjectileClass >= armorComp.ArmorClass.Value)
                    ignore.Add(entity.Value);
        }

        if (TryComp<DamageableComponent>(target, out var damageable) && damageable.DamageModifierSetId != null)
            if (_prototype.TryIndex(damageable.DamageModifierSetId, out var damageModifierSetPrototype))
                ignoreResitance = component.ProjectileClass >= damageModifierSetPrototype.Class;
        // stalker-changes-end

        // it's here so this check is only done once before possible hit
        var attemptEv = new ProjectileReflectAttemptEvent(uid, component, false);
        RaiseLocalEvent(target, ref attemptEv);
        if (attemptEv.Cancelled)
        {
            SetShooter(uid, component, target);
            return;
        }

        var ev = new ProjectileHitEvent(component.Damage * _damageableSystem.UniversalProjectileDamageModifier, target, component.Shooter);
        RaiseLocalEvent(uid, ref ev);

        var otherName = ToPrettyString(target);
        var damageRequired = _destructibleSystem.DestroyedAt(target);
        if (TryComp<DamageableComponent>(target, out var damageableComponent))
        {
            damageRequired -= damageableComponent.TotalDamage;
            damageRequired = FixedPoint2.Max(damageRequired, FixedPoint2.Zero);
        }
        var deleted = Deleted(target);

        var damageApplied = _damageableSystem.TryChangeDamage((target, damageableComponent), ev.Damage, out var damage, component.IgnoreResistances || ignoreResitance, origin: component.Shooter, ignoreResistors: ignore); // Stalker-Changes-IgnoreResistors
        if (damageApplied && Exists(component.Shooter))
        {
            if (!deleted && !predicted)
            {
                _color.RaiseEffect(Color.Red, new List<EntityUid> { target }, Filter.Pvs(target, entityManager: EntityManager));
            }

            _adminLogger.Add(LogType.BulletHit,
                LogImpact.Medium,
                $"Projectile {ToPrettyString(uid):projectile} shot by {ToPrettyString(component.Shooter!.Value):user} hit {otherName:target} and dealt {damage:damage} damage");

            // If penetration is to be considered, we need to do some checks to see if the projectile should stop.
            if (component.PenetrationThreshold != 0)
            {
                // If a damage type is required, stop the bullet if the hit entity doesn't have that type.
                if (component.PenetrationDamageTypeRequirement != null)
                {
                    var stopPenetration = false;
                    foreach (var requiredDamageType in component.PenetrationDamageTypeRequirement)
                    {
                        if (!damage.DamageDict.Keys.Contains(requiredDamageType))
                        {
                            stopPenetration = true;
                            break;
                        }
                    }
                    if (stopPenetration)
                        component.ProjectileSpent = true;
                }

                // If the object won't be destroyed, it "tanks" the penetration hit.
                if (damage.GetTotal() < damageRequired)
                {
                    component.ProjectileSpent = true;
                }

                if (!component.ProjectileSpent)
                {
                    component.PenetrationAmount += damageRequired;
                    // The projectile has dealt enough damage to be spent.
                    if (component.PenetrationAmount >= component.PenetrationThreshold)
                    {
                        component.ProjectileSpent = true;
                    }
                }
            }
            else
            {
                component.ProjectileSpent = true;
            }
        }
        else
        {
            // Zona14: damage didn't apply (no DamageableComponent on target, damage spec rejected,
            // or shooter de-spawned mid-flight). The bullet still physically struck a hard fixture
            // — without this, ProjectileSpent stays false and the DeleteOnCollide check below
            // never QueueDels the projectile, so it sits stuck against the target. Penetration
            // tracking depends on `damage` info we don't have in this branch, so just mark spent.
            component.ProjectileSpent = true;
        }

        if (!deleted)
        {
            _guns.PlayImpactSound(target, damage, component.SoundHit, component.ForceSound);

            if (!predicted && !ourBody.LinearVelocity.IsLengthZero())
                _sharedCameraRecoil.KickCamera(target, ourBody.LinearVelocity.Normalized());
        }

        if (component.DeleteOnCollide && component.ProjectileSpent)
            QueueDel(uid);

        if (!predicted && component.ImpactEffect != null && TryComp(uid, out TransformComponent? xform))
        {
            var filter = Filter.Pvs(xform.Coordinates, entityMan: EntityManager);
            // Zona14: exclude the shooter — their client twin already raised a local
            // ImpactEffectEvent via SharedProjectileSystem.ProjectileCollide's IsClientSide
            // branch. Without this, the shooter sees two impact effects: one immediate from
            // the predicted twin, one a frame or two later from the server broadcast.
            if (component.Shooter is { } shooter && TryComp(shooter, out ActorComponent? actor))
                filter = filter.RemovePlayer(actor.PlayerSession);
            RaiseNetworkEvent(new ImpactEffectEvent(component.ImpactEffect, GetNetCoordinates(xform.Coordinates)), filter);
        }
    }
}
