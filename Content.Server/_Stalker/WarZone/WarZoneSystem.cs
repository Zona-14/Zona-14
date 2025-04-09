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
using Content.Shared.Physics;
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

    private readonly Dictionary<EntityUid, CaptureState> _activeCaptures = new();
    private readonly Dictionary<EntityUid, TimeSpan> _lastRewardTimes = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WarZoneComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<WarZoneComponent, EndCollideEvent>(OnEndCollide);

        SubscribeLocalEvent<MetaDataComponent, EntityTerminatingEvent>(OnEntityTerminating);
        // Removed MobStateChangedEvent subscription due to missing type

        _ = SyncFactionsAndBandsWithDatabase();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;

        foreach (var (zone, state) in _activeCaptures)
        {
            _ = UpdateCaptureAsync(zone, state, now);
        }

        foreach (var (zone, lastRewardTime) in _lastRewardTimes)
        {
            DistributeRewards(zone, lastRewardTime, now);
        }
    }

    private async Task UpdateCaptureAsync(EntityUid zone, CaptureState state, TimeSpan now)
    {
        var frameTimeSec = (float)_gameTiming.FrameTime.TotalSeconds;

        if (!_entityManager.TryGetComponent(zone, out WarZoneComponent? wzComp) ||
            !_prototypeManager.TryIndex<STWarZonePrototype>(wzComp.ZoneProto, out var wzProto))
            return;

        if (state.PresentBandIds.Count > 1 || state.PresentFactionIds.Count > 1)
        {
            ResetAllRequirements(zone);
            return;
        }

        if (state.PresentBandIds.Count == 0 && state.PresentFactionIds.Count == 0)
        {
            ResetAllRequirements(zone);

            if (state.CurrentAttackerBandId != null || state.CurrentAttackerFactionId != null)
            {
                _chatManager.DispatchServerAnnouncement($"Capture attempt on '{wzComp.PortalName}' was abandoned!");
                state.CurrentAttackerBandId = null;
                state.CurrentAttackerFactionId = null;
                state.PresentBandIds.Clear();
                state.PresentFactionIds.Clear();
            }

            return;
        }

        int? attackerBand = null;
        int? attackerFaction = null;

        if (state.PresentBandIds.Count == 1)
            attackerBand = GetFirst(state.PresentBandIds);
        if (state.PresentFactionIds.Count == 1)
            attackerFaction = GetFirst(state.PresentFactionIds);

        if ((attackerBand != null && attackerBand == state.DefendingBandId) ||
            (attackerFaction != null && attackerFaction == state.DefendingFactionId))
        {
            ResetAllRequirements(zone);
            return;
        }

        if ((attackerBand != state.CurrentAttackerBandId || attackerFaction != state.CurrentAttackerFactionId) &&
            (attackerBand != null || attackerFaction != null))
        {
            state.CurrentAttackerBandId = attackerBand;
            state.CurrentAttackerFactionId = attackerFaction;

            var announce = $"Capture attempt started on '{wzComp.PortalName}'!";
            _chatManager.DispatchServerAnnouncement(announce);
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
                if (!req.Check(attackerBand, attackerFaction, ownerships, lastCaptureTimes, zonePrototypes, wzComp.ZoneProto, frameTimeSec))
                {
                    allMet = false;
                    break;
                }
            }
        }

        if (!allMet)
            return;

        state.DefendingBandId = attackerBand;
        state.DefendingFactionId = attackerFaction;

        // Find the band and faction prototype IDs from the database IDs
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

        // If both band and faction are present, prefer faction ownership
        if (bandProtoId != null && factionProtoId != null)
        {
            bandProtoId = null;
        }

        await _dbManager.SetStalkerZoneOwnershipAsync(
            wzComp.ZoneProto,
            bandProtoId,
            factionProtoId);

        var msg = $"Zone '{wzComp.PortalName}' captured!";
        _chatManager.DispatchServerAnnouncement(msg);
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

        var period = wzProto.RewardPeriod;

        if (now - lastRewardTime < period)
            return;

        if (!_activeCaptures.TryGetValue(zone, out var state))
            return;

        var points = wzProto.RewardPointsPerPeriod;

        if (state.DefendingBandId.HasValue)
        {
            foreach (var bandProto in _prototypeManager.EnumeratePrototypes<STBandPrototype>())
            {
                if (bandProto.DatabaseId == state.DefendingBandId.Value)
                {
                    _dbManager.SetStalkerBandAsync(new ProtoId<STBandPrototype>(bandProto.ID), points);
                    break;
                }
            }
        }
        else if (state.DefendingFactionId.HasValue)
        {
            foreach (var factionProto in _prototypeManager.EnumeratePrototypes<NpcFactionPrototype>())
            {
                if (factionProto.DatabaseId == state.DefendingFactionId.Value)
                {
                    _dbManager.SetStalkerFactionAsync(new ProtoId<NpcFactionPrototype>(factionProto.ID), points);
                    break;
                }
            }
        }

        _lastRewardTimes[zone] = now;
    }

    private static int? GetFirst(HashSet<int> set)
    {
        foreach (var g in set)
            return g;
        return null;
    }

    private void OnStartCollide(EntityUid uid, WarZoneComponent component, ref StartCollideEvent args)
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

        if (!_activeCaptures.TryGetValue(uid, out var state))
        {
            state = new CaptureState();
            _activeCaptures[uid] = state;
        }

        if (bandDbId.HasValue)
            state.PresentBandIds.Add(bandDbId.Value);
        if (factionDbId.HasValue)
            state.PresentFactionIds.Add(factionDbId.Value);
    }

    private void OnEndCollide(EntityUid uid, WarZoneComponent component, ref EndCollideEvent args)
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

        if (!_activeCaptures.TryGetValue(uid, out var state))
            return;

        if (bandDbId.HasValue)
            state.PresentBandIds.Remove(bandDbId.Value);
        if (factionDbId.HasValue)
            state.PresentFactionIds.Remove(factionDbId.Value);
    }

    private void OnEntityTerminating(EntityUid uid, MetaDataComponent component, ref EntityTerminatingEvent args)
    {
        RemoveEntityFromAllCaptures(uid);
    }

    // Removed OnMobStateChanged due to missing MobStateChangedEvent type

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

        foreach (var (zone, state) in _activeCaptures)
        {
            bool changed = false;

            if (bandDbId.HasValue && state.PresentBandIds.Remove(bandDbId.Value))
                changed = true;

            if (factionDbId.HasValue && state.PresentFactionIds.Remove(factionDbId.Value))
                changed = true;

            if (changed && state.PresentBandIds.Count == 0 && state.PresentFactionIds.Count == 0)
            {
                ResetAllRequirements(zone);

                if (_entityManager.TryGetComponent(zone, out WarZoneComponent? wzComp))
                {
                    _chatManager.DispatchServerAnnouncement($"Capture attempt on '{wzComp.PortalName}' was abandoned!");
                }

                state.CurrentAttackerBandId = null;
                state.CurrentAttackerFactionId = null;
                state.PresentBandIds.Clear();
                state.PresentFactionIds.Clear();
            }
        }
    }

    private async Task SyncFactionsAndBandsWithDatabase()
    {
        try
        {
            foreach (var bandProto in _prototypeManager.EnumeratePrototypes<STBandPrototype>())
            {
                var band = await _dbManager.GetStalkerBandAsync(bandProto.ID);
                if (band == null)
                {
                    await _dbManager.SetStalkerBandAsync(bandProto.ID, 0);
                }
            }

            foreach (var factionProto in _prototypeManager.EnumeratePrototypes<NpcFactionPrototype>())
            {
                var faction = await _dbManager.GetStalkerFactionAsync(factionProto.ID);
                if (faction == null)
                {
                    await _dbManager.SetStalkerFactionAsync(factionProto.ID, 0);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.ErrorS("warzone", $"Error syncing factions/bands with DB: {ex}");
        }
    }

    private sealed class CaptureState
    {
        public int? DefendingBandId;
        public int? DefendingFactionId;

        public int? CurrentAttackerBandId;
        public int? CurrentAttackerFactionId;

        public HashSet<int> PresentBandIds = new();
        public HashSet<int> PresentFactionIds = new();
    }
}