using Content.Server.Stunnable;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Throwing;
using Robust.Server.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Server.NPC.HTN;
using Robust.Shared.Prototypes;
namespace Content.Server._Stalker.NPCs;

public sealed class NPCBloodsuckerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly StunSystem _stunSystem = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly TransformSystem _xform = default!;


    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Tries to use bloodsuck attack on the current target.
        var query = EntityQueryEnumerator<NPCBloodsuckerComponent, HTNComponent>();
        while (query.MoveNext(out var uid, out var comp, out var htn))
        {
            if (!htn.Blackboard.TryGetValue<EntityUid>(comp.TargetKey, out var target, EntityManager))
                continue;

            TryBloodsuck((uid, comp), target);
        }
    }

    public void TryBloodsuck(Entity<NPCBloodsuckerComponent?> user, EntityUid target)
    {

        if (!Resolve(user, ref user.Comp, false) || user.Comp.NextTimeUpdate > _timing.CurTime)
            return;

        user.Comp.NextTimeUpdate = _timing.CurTime + TimeSpan.FromSeconds(user.Comp.UpdateCooldown);

        if (CheckValidTarget(target, user.Comp) is not { } validTarget)
            return;

        if (_timing.CurTime > user.Comp.EndTime)
        {
            user.Comp.StartTime = _timing.CurTime;
            user.Comp.EndTime = user.Comp.StartTime + TimeSpan.FromSeconds(user.Comp.ReloadTime + _random.NextFloat(-user.Comp.RandomiseReloadTime, user.Comp.RandomiseReloadTime));

            if (TryComp<MobStateComponent>(target, out var entityMobState) && _mobState.IsAlive(target, entityMobState))
            {
                _stunSystem.TrySlowdown(target, TimeSpan.FromSeconds(user.Comp.StunTime), false, 0f, 0f); // 0f 0f since we want them to stay still and prevent any moving
                _stunSystem.TrySlowdown(user, TimeSpan.FromSeconds(user.Comp.StunTime), false, 0f, 0f); // 0f 0f since we want them to stay still and prevent any moving

                _damage.TryChangeDamage(target, user.Comp.DamageOnSuck, true, origin: user); // damage Target
                _damage.TryChangeDamage(user, user.Comp.HealOnSuck, true, origin: target); // heal User
            }
        }
    }

    // Util from jumpscare stuff, iterating thru entity so we can confirm that entity is in range
    private EntityUid? CheckValidTarget(EntityUid uid, NPCBloodsuckerComponent component)
    {
        var closestDistance = float.MaxValue;
        EntityUid? target = null;

        var entities = new HashSet<Entity<MobStateComponent>>();
        var xform = Transform(uid);
        var mapCoords = _xform.ToMapCoordinates(xform.Coordinates);
        _lookup.GetEntitiesInRange(mapCoords, component.AttackRadius, entities, LookupFlags.Dynamic);

        foreach (var entity in entities)
        {
            if (!HasComp<HumanoidAppearanceComponent>(entity))
                continue;

            if (!_mobState.IsAlive(entity, entity.Comp))
                continue;

            var dist = (_xform.GetWorldPosition(uid) - _xform.GetWorldPosition(entity)).Length();
            if (dist >= closestDistance)
                continue;

            target = entity;
            closestDistance = dist;
        }

        return target;
    }
}
