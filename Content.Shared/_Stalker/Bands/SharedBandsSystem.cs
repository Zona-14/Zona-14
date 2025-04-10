using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Content.Shared.Players.JobWhitelist;
using Content.Shared.Actions;

namespace Content.Shared._Stalker.Bands
{
    public sealed class SharedBandsSystem : EntitySystem
    {
        [Dependency] private readonly SharedActionsSystem _actions = default!;
        [Dependency] private readonly MobStateSystem _mobState = default!;
        [Dependency] private readonly IPrototypeManager _protoManager = default!;
        [Dependency] private readonly ISharedPlayerManager _playerManager = default!;
        [Dependency] private readonly JobWhitelistManager _jobWhitelist = default!;

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

        public STBandPrototype? GetPlayerBand(NetUserId userId)
        {
            if (!_playerManager.TryGetSessionById(userId, out var session))
                return null;

            var roles = _jobWhitelist.GetWhitelistedJobs(userId);
            if (roles.Count == 0)
                return null;

            var primaryRole = roles.First();

            foreach (var bandProto in _protoManager.EnumeratePrototypes<STBandPrototype>())
            {
                if (bandProto.Hierarchy.Values.Contains(primaryRole))
                    return bandProto;
            }

            if (_protoManager.TryIndex<STBandPrototype>(primaryRole, out var fallbackBand))
                return fallbackBand;

            return null;
        }

        public List<BandMemberInfo> GetBandMembers(NetUserId userId)
        {
            var result = new List<BandMemberInfo>();

            var playerBand = GetPlayerBand(userId);
            if (playerBand == null)
                return result;

            var bandRoles = playerBand.Hierarchy.Values.ToHashSet();

            foreach (var session in _playerManager.Sessions)
            {
                var memberId = session.UserId;
                var roles = _jobWhitelist.GetWhitelistedJobs(memberId);
                if (roles.Any(role => bandRoles.Contains(role)))
                {
                    result.Add(new BandMemberInfo
                    {
                        UserId = memberId,
                        PlayerName = session.Name,
                        RoleId = roles.FirstOrDefault() ?? string.Empty
                    });
                }
            }

            return result;
        }

        public STBandPrototype? GetBandInfo(NetUserId userId)
        {
            return GetPlayerBand(userId);
        }

        public bool CanManageBand(NetUserId userId)
        {
            var band = GetPlayerBand(userId);
            if (band == null || band.ManagingRankId == null)
                return false;

            if (!_playerManager.TryGetSessionById(userId, out var session))
                return false;

            var roles = _jobWhitelist.GetWhitelistedJobs(userId);
            if (roles.Count == 0)
                return false;

            var primaryRole = roles.First();

            return band.Hierarchy.TryGetValue(band.ManagingRankId.Value, out var managingRole) &&
                   managingRole == primaryRole;
        }
    }

    public sealed class BandMemberInfo
    {
        public NetUserId UserId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public string RoleId { get; set; } = string.Empty;
    }
}
