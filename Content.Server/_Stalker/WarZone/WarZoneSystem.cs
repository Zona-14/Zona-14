using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server._Stalker.WarZone;
using Content.Shared._Stalker.WarZone.Requirenments;
using Content.Server.Database;
using Content.Shared._Stalker.WarZone;
using Content.Shared.Physics;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Physics.Events;
using Content.Server.Chat.Managers;
using Content.Server.Popups;
using Content.Shared.Mobs;
using Content.Shared._Stalker.Bands;
using Content.Shared.NPC.Prototypes;

namespace Content.Server._Stalker.WarZone;

public sealed partial class WarZoneSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IServerDbManager _dbManager = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    private readonly Dictionary<int, float> _bandPoints = new();
    private readonly Dictionary<int, float> _factionPoints = new();
    private readonly Dictionary<EntityUid, TimeSpan> _lastRewardTimes = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WarZoneComponent, ComponentInit>(OnWarZoneInit);
        SubscribeLocalEvent<WarZoneComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<WarZoneComponent, EndCollideEvent>(OnEndCollide);
        SubscribeLocalEvent<MetaDataComponent, EntityTerminatingEvent>(OnEntityTerminating);
    }

    private void OnWarZoneInit(EntityUid uid, WarZoneComponent component, ComponentInit args)
    {
        _ = InitializeWarZoneAsync(uid, component);
    }

    private async Task InitializeWarZoneAsync(EntityUid uid, WarZoneComponent component)
    {
        try
        {
            foreach (var bandProto in _prototypeManager.EnumeratePrototypes<STBandPrototype>())
            {
                var band = await _dbManager.GetStalkerBandAsync(bandProto.ID);
                if (band == null)
                {
                    await _dbManager.SetStalkerBandAsync(bandProto.ID, 0);
                    _bandPoints[bandProto.DatabaseId] = 0;
                }
                else
                {
                    _bandPoints[bandProto.DatabaseId] = band.RewardPoints;
                }
            }

            foreach (var factionProto in _prototypeManager.EnumeratePrototypes<NpcFactionPrototype>())
            {
                var faction = await _dbManager.GetStalkerFactionAsync(factionProto.ID);
                if (faction == null)
                {
                    await _dbManager.SetStalkerFactionAsync(factionProto.ID, 0);
                    _factionPoints[factionProto.DatabaseId] = 0;
                }
                else
                {
                    _factionPoints[factionProto.DatabaseId] = faction.RewardPoints;
                }
            }

            component.InitialLoadComplete = false;
            component.PresentBandIds = new();
            component.PresentFactionIds = new();

            _ = LoadInitialZoneStateAsync(uid, component);

            var ownership = await _dbManager.GetStalkerWarOwnershipAsync(component.ZoneProto);
            if (ownership != null && (ownership.BandId != null || ownership.FactionId != null))
            {
                var lastRewardTime = ownership.LastCapturedByCurrentOwnerAt.HasValue
                    ? _gameTiming.CurTime - (DateTime.UtcNow - ownership.LastCapturedByCurrentOwnerAt.Value)
                    : _gameTiming.CurTime;

                _lastRewardTimes[uid] = lastRewardTime;

                Logger.InfoS("warzone", $"Initialized reward timing for zone '{component.PortalName}', owned by {(ownership.BandId != null ? $"band:{ownership.BandId}" : $"faction:{ownership.FactionId}")}");
            }
        }
        catch (Exception ex)
        {
            Logger.ErrorS("warzone", $"Error initializing war zone {component.ZoneProto}: {ex}");
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;

        var query = EntityQueryEnumerator<WarZoneComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            _ = UpdateCaptureAsync(uid, comp, now);
        }

        foreach (var (zone, lastRewardTime) in _lastRewardTimes)
        {
            DistributeRewards(zone, lastRewardTime, now);
        }
    }

    private async Task UpdateCaptureAsync(EntityUid zone, WarZoneComponent comp, TimeSpan now)
    {
        var frameTimeSec = (float)_gameTiming.FrameTime.TotalSeconds;

        if (!_prototypeManager.TryIndex<STWarZonePrototype>(comp.ZoneProto, out var wzProto))
            return;

        if (comp.PresentBandIds.Count > 1 || comp.PresentFactionIds.Count > 1)
        {
            ResetAllRequirements(zone);
            return;
        }

        if (comp.PresentBandIds.Count == 0 && comp.PresentFactionIds.Count == 0)
        {
            ResetAllRequirements(zone);

            if (comp.CurrentAttackerBandId != null || comp.CurrentAttackerFactionId != null)
            {
                if ((comp.CurrentAttackerBandId != comp.DefendingBandId || comp.CurrentAttackerFactionId != comp.DefendingFactionId) &&
                    (comp.CurrentAttackerBandId != null || comp.CurrentAttackerFactionId != null))
                {
                    string attackerName = GetAttackerName(comp.CurrentAttackerBandId, comp.CurrentAttackerFactionId);
                    _chatManager.DispatchServerAnnouncement($"Capture attempt on '{comp.PortalName}' by {attackerName} was abandoned!");
                }
                comp.CurrentAttackerBandId = null;
                comp.CurrentAttackerFactionId = null;
                comp.PresentBandIds.Clear();
                comp.PresentFactionIds.Clear();
            }

            return;
        }

        int? attackerBand = null;
        int? attackerFaction = null;

        if (comp.PresentBandIds.Count == 1)
            attackerBand = GetFirst(comp.PresentBandIds);
        if (comp.PresentFactionIds.Count == 1)
            attackerFaction = GetFirst(comp.PresentFactionIds);

        if ((attackerBand != null && attackerBand == comp.DefendingBandId) ||
            (attackerFaction != null && attackerFaction == comp.DefendingFactionId))
        {
            ResetAllRequirements(zone);
            return;
        }

        bool isDefender = (attackerBand != null && attackerBand == comp.DefendingBandId) ||
                          (attackerFaction != null && attackerFaction == comp.DefendingFactionId);

        bool zoneCooldownActive = comp.CooldownEndTime.HasValue && now < comp.CooldownEndTime.Value;

        if (!isDefender && !zoneCooldownActive && comp.InitialLoadComplete &&
            (attackerBand != comp.CurrentAttackerBandId || attackerFaction != comp.CurrentAttackerFactionId) &&
            (attackerBand != null || attackerFaction != null))
        {
            comp.CurrentAttackerBandId = attackerBand;
            comp.CurrentAttackerFactionId = attackerFaction;

            string attackerName = GetAttackerName(attackerBand, attackerFaction);
            _chatManager.DispatchServerAnnouncement($"Capture attempt by {attackerName} started on '{comp.PortalName}'!");
        }

        var ownerships = new Dictionary<ProtoId<STWarZonePrototype>, (int? BandId, int? FactionId)>();
        var lastCaptureTimes = new Dictionary<ProtoId<STWarZonePrototype>, DateTime?>();
        var zonePrototypes = new Dictionary<ProtoId<STWarZonePrototype>, STWarZonePrototype>();

        var requiredZoneIds = new HashSet<ProtoId<STWarZonePrototype>>();

        if (wzProto.Requirements != null)
        {
            foreach (var req in wzProto.Requirements)
            {
                if (req is ZoneOwnershipRequirenment zoneReq)
                {
                    foreach (var rid in zoneReq.RequiredZones)
                        requiredZoneIds.Add(rid);
                }
            }
        }

        foreach (var rid in requiredZoneIds)
        {
            var ownership = await _dbManager.GetStalkerWarOwnershipAsync(rid);
            if (ownership != null)
            {
                ownerships[rid] = (ownership.BandId, ownership.FactionId);
                lastCaptureTimes[rid] = ownership.LastCapturedByCurrentOwnerAt;
            }

            if (_prototypeManager.TryIndex<STWarZonePrototype>(rid, out var reqProto))
            {
                zonePrototypes[rid] = reqProto;
            }
        }

        var allMet = true;

        if (wzProto.Requirements != null)
        {
            foreach (var req in wzProto.Requirements)
            {
                var blockReason = req.Check(attackerBand, attackerFaction, ownerships, lastCaptureTimes, zonePrototypes, comp.ZoneProto, frameTimeSec);
                if (blockReason != CaptureBlockReason.None)
                {
                    allMet = false;
                    break;
                }
            }
        }

        if (!allMet)
            return;

        comp.DefendingBandId = attackerBand;
        comp.DefendingFactionId = attackerFaction;

        ProtoId<STBandPrototype>? bandProtoId = null;
        ProtoId<NpcFactionPrototype>? factionProtoId = null;

        if (attackerBand.HasValue)
        {
            foreach (var bandProto in _prototypeManager.EnumeratePrototypes<STBandPrototype>())
            {
                if (bandProto.DatabaseId == attackerBand.Value)
                {
                    bandProtoId = bandProto.ID;
                    break;
                }
            }
        }

        if (attackerFaction.HasValue)
        {
            foreach (var factionProto in _prototypeManager.EnumeratePrototypes<NpcFactionPrototype>())
            {
                if (factionProto.DatabaseId == attackerFaction.Value)
                {
                    factionProtoId = factionProto.ID;
                    break;
                }
            }
        }

        if (bandProtoId != null && factionProtoId != null)
            bandProtoId = null;

        await _dbManager.SetStalkerZoneOwnershipAsync(
            comp.ZoneProto,
            bandProtoId,
            factionProtoId);

        if (wzProto.CaptureCooldownHours > 0)
        {
            comp.CooldownEndTime = _gameTiming.CurTime + TimeSpan.FromHours(wzProto.CaptureCooldownHours);
        }

        string defenderName = GetAttackerName(comp.DefendingBandId, comp.DefendingFactionId);
        _chatManager.DispatchServerAnnouncement($"Zone '{comp.PortalName}' captured by {defenderName}!");

        // Reset reward timer on capture
        _lastRewardTimes[zone] = _gameTiming.CurTime;
    }

    private void ResetAllRequirements(EntityUid zone)
    {
        if (!_entityManager.TryGetComponent(zone, out WarZoneComponent? wzComp))
            return;

        if (!_prototypeManager.TryIndex<STWarZonePrototype>(wzComp.ZoneProto, out var wzProto))
            return;

        if (wzProto.Requirements == null)
            return;

        foreach (var req in wzProto.Requirements)
        {
            if (req is CaptureTimeRequirenment captureReq)
                captureReq.Reset();
        }
    }

    private void DistributeRewards(EntityUid zone, TimeSpan lastRewardTime, TimeSpan now)
    {
        if (!_entityManager.TryGetComponent(zone, out WarZoneComponent? wzComp))
            return;

        if (!_prototypeManager.TryIndex<STWarZonePrototype>(wzComp.ZoneProto, out var wzProto))
            return;

        var period = TimeSpan.FromSeconds(wzProto.RewardPeriod);

        if (now - lastRewardTime < period)
            return;

        if (wzComp.DefendingBandId == null && wzComp.DefendingFactionId == null && wzProto.ShouldAwardWhenDefenderPresent)
            return;

        var points = wzProto.RewardPointsPerPeriod;
        bool rewarded = false;

        if (wzComp.DefendingBandId.HasValue)
        {
            var bandDbId = wzComp.DefendingBandId.Value;
            foreach (var bandProto in _prototypeManager.EnumeratePrototypes<STBandPrototype>())
            {
                if (bandProto.DatabaseId == bandDbId)
                {
                    var currentPoints = _bandPoints.TryGetValue(bandDbId, out var val) ? val : 0;
                    var newPoints = currentPoints + (int)points;
                    _bandPoints[bandDbId] = newPoints;
                    _dbManager.SetStalkerBandAsync(new ProtoId<STBandPrototype>(bandProto.ID), newPoints);
                    Logger.InfoS("warzone", $"Awarded {points} points to band {bandProto.Name} (total: {newPoints}) for controlling {wzComp.PortalName}");
                    rewarded = true;
                    break;
                }
            }
        }
        else if (wzComp.DefendingFactionId.HasValue)
        {
            var factionDbId = wzComp.DefendingFactionId.Value;
            foreach (var factionProto in _prototypeManager.EnumeratePrototypes<NpcFactionPrototype>())
            {
                if (factionProto.DatabaseId == factionDbId)
                {
                    var currentPoints = _factionPoints.TryGetValue(factionDbId, out var val) ? val : 0;
                    var newPoints = currentPoints + (int)points;
                    _factionPoints[factionDbId] = newPoints;
                    _dbManager.SetStalkerFactionAsync(new ProtoId<NpcFactionPrototype>(factionProto.ID), newPoints);
                    Logger.InfoS("warzone", $"Awarded {points} points to faction {factionProto.ID} (total: {newPoints}) for controlling {wzComp.PortalName}");
                    rewarded = true;
                    break;
                }
            }
        }

        if (rewarded)
        {
            _lastRewardTimes[zone] = now;
        }
    }

    private static int? GetFirst(HashSet<int> set)
    {
        foreach (var g in set)
            return g;
        return null;
    }

    private void OnStartCollide(EntityUid uid, WarZoneComponent component, ref readonly StartCollideEvent args)
    {
        var other = args.OtherEntity;

        if (!_entityManager.TryGetComponent(other, out BandsComponent? bands))
            return;

        var bandProtoId = bands.BandProto;
        int? bandDbId = null;
        int? factionDbId = null;

        if (_prototypeManager.TryIndex<STBandPrototype>(bandProtoId, out var bandProto))
        {
            bandDbId = bandProto.DatabaseId;

            if (_prototypeManager.TryIndex<NpcFactionPrototype>(bandProto.FactionId, out var factionProto))
            {
                factionDbId = factionProto.DatabaseId;
            }
        }

        if (component.PresentBandIds == null)
            component.PresentBandIds = new();
        if (component.PresentFactionIds == null)
            component.PresentFactionIds = new();

        var now = _gameTiming.CurTime;
        if (component.CooldownEndTime.HasValue && now < component.CooldownEndTime.Value)
        {
            var remainingTime = component.CooldownEndTime.Value - now;
            var portalName = component.PortalName ?? "Unknown Zone";
            var message = $"Zone '{portalName}' is on cooldown. Next capture available in {remainingTime.TotalMinutes:F1} minutes.";
            _popup.PopupEntity(message, other);
        }

        if (bandDbId.HasValue)
            component.PresentBandIds.Add(bandDbId.Value);
        if (factionDbId.HasValue)
            component.PresentFactionIds.Add(factionDbId.Value);
    }

    private void OnEndCollide(EntityUid uid, WarZoneComponent component, ref readonly EndCollideEvent args)
    {
        var other = args.OtherEntity;

        if (!_entityManager.TryGetComponent(other, out BandsComponent? bands))
            return;

        var bandProtoId = bands.BandProto;
        int? bandDbId = null;
        int? factionDbId = null;

        if (_prototypeManager.TryIndex<STBandPrototype>(bandProtoId, out var bandProto))
        {
            bandDbId = bandProto.DatabaseId;

            if (_prototypeManager.TryIndex<NpcFactionPrototype>(bandProto.FactionId, out var factionProto))
            {
                factionDbId = factionProto.DatabaseId;
            }
        }

        if (component.PresentBandIds == null || component.PresentFactionIds == null)
            return;

        if (bandDbId.HasValue)
            component.PresentBandIds.Remove(bandDbId.Value);
        if (factionDbId.HasValue)
            component.PresentFactionIds.Remove(factionDbId.Value);
    }

    private void OnEntityTerminating(EntityUid uid, MetaDataComponent component, ref EntityTerminatingEvent args)
    {
        RemoveEntityFromAllCaptures(uid);
    }

    private void RemoveEntityFromAllCaptures(EntityUid uid)
    {
        if (!_entityManager.TryGetComponent(uid, out BandsComponent? bands))
            return;

        int? bandDbId = null;
        int? factionDbId = null;

        if (_prototypeManager.TryIndex<STBandPrototype>(bands.BandProto, out var bandProto))
        {
            bandDbId = bandProto.DatabaseId;

            if (_prototypeManager.TryIndex<NpcFactionPrototype>(bandProto.FactionId, out var factionProto))
            {
                factionDbId = factionProto.DatabaseId;
            }
        }

        var query = EntityQueryEnumerator<WarZoneComponent>();
        while (query.MoveNext(out var zoneUid, out var wzComp))
        {
            bool changed = false;

            if (wzComp.PresentBandIds != null && bandDbId.HasValue && wzComp.PresentBandIds.Remove(bandDbId.Value))
                changed = true;

            if (wzComp.PresentFactionIds != null && factionDbId.HasValue && wzComp.PresentFactionIds.Remove(factionDbId.Value))
                changed = true;

            // Add null checks before accessing Count
            if (changed &&
                wzComp.PresentBandIds != null &&
                wzComp.PresentFactionIds != null &&
                wzComp.PresentBandIds.Count == 0 &&
                wzComp.PresentFactionIds.Count == 0)
            {
                ResetAllRequirements(zoneUid);

                if (wzComp != null && (wzComp.CurrentAttackerBandId != null || wzComp.CurrentAttackerFactionId != null))
                {
                    string attackerName = GetAttackerName(wzComp.CurrentAttackerBandId, wzComp.CurrentAttackerFactionId);
                    _chatManager.DispatchServerAnnouncement($"Capture attempt on '{wzComp.PortalName}' by {attackerName} was abandoned!");
                }

                if (wzComp != null)
                {
                    wzComp.CurrentAttackerBandId = null;
                    wzComp.CurrentAttackerFactionId = null;
                    if (wzComp.PresentBandIds != null)
                        wzComp.PresentBandIds.Clear();
                    if (wzComp.PresentFactionIds != null)
                        wzComp.PresentFactionIds.Clear();
                }
            }
        }
    }

    private string GetAttackerName(int? bandId, int? factionId)
    {
        if (bandId.HasValue)
        {
            foreach (var bandProto in _prototypeManager.EnumeratePrototypes<STBandPrototype>())
            {
                if (bandProto.DatabaseId == bandId.Value)
                    return bandProto.Name;
            }
        }
        else if (factionId.HasValue)
        {
            foreach (var factionProto in _prototypeManager.EnumeratePrototypes<NpcFactionPrototype>())
            {
                if (factionProto.DatabaseId == factionId.Value)
                    return factionProto.ID;
            }
        }
        return "Unknown";
    }

    private async Task LoadInitialZoneStateAsync(EntityUid zoneUid, WarZoneComponent component)
    {
        try
        {
            if (!_prototypeManager.TryIndex<STWarZonePrototype>(component.ZoneProto, out var wzProto))
            {
                Logger.ErrorS("warzone", $"Could not find STWarZonePrototype with ID '{component.ZoneProto}' during async state load for zone {zoneUid}.");
                return;
            }

            var ownership = await _dbManager.GetStalkerWarOwnershipAsync(component.ZoneProto);

            if (ownership != null)
            {
                // Store raw DB numerical IDs directly
                component.DefendingBandId = ownership.BandId;
                component.DefendingFactionId = ownership.FactionId;

                if (ownership.LastCapturedByCurrentOwnerAt.HasValue && wzProto.CaptureCooldownHours > 0)
                {
                    DateTime captureTime = ownership.LastCapturedByCurrentOwnerAt.Value;
                    DateTime cooldownEndDateTime = captureTime.AddHours(wzProto.CaptureCooldownHours);
                    DateTime currentDateTime = DateTime.UtcNow;

                    if (cooldownEndDateTime > currentDateTime)
                    {
                        TimeSpan remainingCooldown = cooldownEndDateTime - currentDateTime;
                        component.CooldownEndTime = _gameTiming.CurTime + remainingCooldown;
                    }
                }
            }

            component.InitialLoadComplete = true;
        }
        catch (Exception ex)
        {
            component.InitialLoadComplete = true;
            Logger.ErrorS("warzone", $"Exception during async zone state load for {zoneUid} ({component.ZoneProto}): {ex}");
        }
    }
}