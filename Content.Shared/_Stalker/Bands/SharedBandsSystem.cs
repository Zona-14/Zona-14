using System;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Content.Shared.Actions;
using Robust.Shared.Serialization;
using Robust.Shared.Network;

namespace Content.Shared._Stalker.Bands
{
    [Virtual]
    public class SharedBandsSystem : EntitySystem
    {
        [Dependency] private readonly SharedActionsSystem _actions = default!;
        [Dependency] private readonly MobStateSystem _mobState = default!;
        [Dependency] private readonly IPrototypeManager _protoManager = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<BandsComponent, ComponentInit>(OnInit);
            SubscribeLocalEvent<BandsComponent, ComponentRemove>(OnRemove);
            SubscribeLocalEvent<BandsComponent, ToggleBandsEvent>(OnToggle);
            SubscribeLocalEvent<BandsComponent, ChangeBandEvent>(OnChange);
        }

        private void OnInit(EntityUid uid, BandsComponent component, ComponentInit args)
        {
            EnsureComp<StatusIconComponent>(uid);

            var proto = _protoManager.Index<JobIconPrototype>(component.BandStatusIcon);

            if (component is { AltBand: not null, CanChange: true })
                _actions.AddAction(uid, ref component.ActionChangeEntity, component.ActionChange, uid);

            _actions.AddAction(uid, ref component.ActionEntity, component.Action, uid);
        }

        private void OnChange(Entity<BandsComponent> entity, ref ChangeBandEvent args)
        {
            var comp = entity.Comp;
            if (comp.AltBand == null || !comp.CanChange)
                return;

            (comp.BandStatusIcon, comp.AltBand) = (comp.AltBand, comp.BandStatusIcon);
            Dirty(entity);
            args.Handled = true;
        }

        private void OnRemove(EntityUid uid, BandsComponent component, ComponentRemove args)
        {
            RemComp<StatusIconComponent>(uid);

            var proto = _protoManager.Index<JobIconPrototype>(component.BandStatusIcon);

            _actions.RemoveAction(uid, component.ActionEntity);
            if (component.ActionChangeEntity != null)
                _actions.RemoveAction(uid, component.ActionChangeEntity);
        }

        private void OnToggle(EntityUid uid, BandsComponent component, ToggleBandsEvent args)
        {
            if (!_mobState.IsAlive(uid))
                return;

            var proto = _protoManager.Index<JobIconPrototype>(component.BandStatusIcon);

            component.Enabled = !component.Enabled;
            Dirty(uid, component);

            args.Handled = true;
        }
    }
}

[Serializable, NetSerializable]
public sealed class BandsManagingBoundUserInterfaceState : BoundUserInterfaceState
{
    public string? BandName { get; }
    public int MaxMembers { get; }
    public List<BandMemberInfo> Members { get; }
    public bool CanManage { get; }

    public BandsManagingBoundUserInterfaceState(string? bandName, int maxMembers, List<BandMemberInfo> members, bool canManage)
    {
        BandName = bandName;
        MaxMembers = maxMembers;
        Members = members;
        CanManage = canManage;
    }
}

[Serializable, NetSerializable]
public sealed class BandMemberInfo
{
    public NetUserId UserId { get; }
    public string PlayerName { get; }
    public string RoleId { get; }

    public BandMemberInfo(NetUserId userId, string playerName, string roleId)
    {
        UserId = userId;
        PlayerName = playerName;
        RoleId = roleId;
    }
}

[Serializable, NetSerializable]
public sealed class BandsManagingAddMemberMessage : BoundUserInterfaceMessage
{
    public string PlayerName { get; }

    public BandsManagingAddMemberMessage(string playerName)
    {
        PlayerName = playerName;
    }
}

[Serializable, NetSerializable]
public sealed class BandsManagingRemoveMemberMessage : BoundUserInterfaceMessage
{
    public Guid PlayerUserId { get; }

    public BandsManagingRemoveMemberMessage(Guid playerUserId)
    {
        PlayerUserId = playerUserId;
    }
}
