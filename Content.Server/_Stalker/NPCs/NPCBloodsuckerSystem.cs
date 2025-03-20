using Content.Server.Stunnable;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Server.NPC.HTN;
using Robust.Shared.Prototypes;
using Content.Server.Actions;
using Content.Server.Animals.Components;
using Content.Server.Popups;
using Content.Shared.Actions.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Storage;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Random;

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
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

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
        if (!Resolve(user, ref user.Comp, false) || user.Comp == null || user.Comp.NextTimeUpdate > _timing.CurTime)
            return;

        user.Comp.NextTimeUpdate = _timing.CurTime + TimeSpan.FromSeconds(user.Comp.UpdateCooldown);

        if (!IsTargetInRange(user, user.Comp, target))
            return;

        if (user.Comp.IsSucking)
        {
            ProcessStages(user, target);
        }
        else if (_timing.CurTime > user.Comp.EndTime)
        {
            StartSucking(user, target);
        }
    }

    private void StartSucking(Entity<NPCBloodsuckerComponent?> user, EntityUid target)
    {
        if (user.Comp == null) return;

        user.Comp.IsSucking = true;
        user.Comp.CurrentStep = 0;
        user.Comp.NextStepTime = _timing.CurTime + TimeSpan.FromSeconds(1);
        user.Comp.StartTime = _timing.CurTime;
        user.Comp.EndTime = user.Comp.StartTime + TimeSpan.FromSeconds(
            user.Comp.ReloadTime + _random.NextFloat(-user.Comp.RandomiseReloadTime, user.Comp.RandomiseReloadTime)
        );

        _stunSystem.TrySlowdown(target, TimeSpan.FromSeconds(user.Comp.StunTime), false, 0f, 0f);
        _stunSystem.TrySlowdown(user.Owner, TimeSpan.FromSeconds(user.Comp.StunTime), false, 0f, 0f);
        _stunSystem.TryStun(user.Owner, TimeSpan.FromSeconds(user.Comp.StunTime), false); // we dont want bloodsucker to attack while suckin
        _stunSystem.TryKnockdown(target, TimeSpan.FromSeconds(0.3), false);

        _audio.PlayPvs(user.Comp.BloodsuckSound, user);
        _popup.PopupEntity(Loc.GetString("action-bloodsucker-sucks-blood"), user, Shared.Popups.PopupType.LargeCaution);
    }

    private void ProcessStages(Entity<NPCBloodsuckerComponent?> user, EntityUid target)
    {
        if (user.Comp == null || user.Comp.NextStepTime > _timing.CurTime)
            return;

        int totalSteps = (int)user.Comp.StunTime;
        if (user.Comp.CurrentStep >= totalSteps)
        {
            user.Comp.IsSucking = false;
            return;
        }

        if (TryComp<MobStateComponent>(target, out var mobState) && _mobState.IsAlive(target, mobState))
        {
            _damage.TryChangeDamage(target, user.Comp.DamageOnSuck, true, origin: user.Owner);
            _damage.TryChangeDamage(user.Owner, user.Comp.HealOnSuck, true, origin: target);
        }

        user.Comp.CurrentStep++;
        user.Comp.NextStepTime = _timing.CurTime + TimeSpan.FromSeconds(1);
    }


    private bool IsTargetInRange(EntityUid uid, NPCBloodsuckerComponent component, EntityUid target)
    {
        if (!HasComp<HumanoidAppearanceComponent>(target))
            return false;

        if (!TryComp<MobStateComponent>(target, out var mobState) || !_mobState.IsAlive(target, mobState))
            return false;

        var distance = (_xform.GetWorldPosition(uid) - _xform.GetWorldPosition(target)).Length();
        return distance <= component.AttackRadius;
    }
}
