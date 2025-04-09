using System;
using System.Collections.Generic;
using Content.Server.Database;
using Content.Shared._Stalker.Bands;
using Content.Shared._Stalker.WarZone;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared._Stalker.WarZone.Requirenments;

namespace Content.Server._Stalker.WarZone;

public sealed class WarpZoneSystem : SharedWarZoneSystem
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IServerDbManager _dbManager = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private TimeSpan _lastUpdateTime;

    public void Initialise()
    {
        SubscribeLocalEvent<WarZoneComponent, ComponentInit>(OnInit);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _lastUpdateTime.Add(TimeSpan.FromSeconds(2f)))
            return;
        _lastUpdateTime = _timing.CurTime;

        var query = EntityQueryEnumerator<WarZoneComponent>();
        while (query.MoveNext(out var uid, out var warZone))
        {
            if (_protoMan.TryIndex(warZone.ZoneProto.Id, out STWarZonePrototype? zoneProto) && zoneProto?.Requirements != null)
            {
                var ownerships = new Dictionary<ProtoId<STWarZonePrototype>, (int? BandId, int? FactionId)>();
                var lastCaptureTimes = new Dictionary<ProtoId<STWarZonePrototype>, DateTime?>();
                var zonePrototypes = new Dictionary<ProtoId<STWarZonePrototype>, STWarZonePrototype>();
                var currentZoneId = warZone.ZoneProto;
                var requiredZoneIds = new HashSet<ProtoId<STWarZonePrototype>>();

                foreach (var req in zoneProto.Requirements)
                {
                    if (req is ZoneOwnershipRequirenment zoneReq)
                    {
                        foreach (var rid in zoneReq.RequiredZones)
                            requiredZoneIds.Add(rid);
                    }
                }

                foreach (var rid in requiredZoneIds)
                {
                    var ownership = _dbManager.GetStalkerWarOwnershipAsync(rid).GetAwaiter().GetResult();
                    if (ownership != null)
                    {
                        ownerships[rid] = (ownership.BandId, ownership.FactionId);
                        lastCaptureTimes[rid] = ownership.LastCapturedByCurrentOwnerAt;
                    }

                    if (_protoMan.TryIndex(rid, out var proto))
                    {
                        zonePrototypes[rid] = proto;
                    }
                }


                int? attackerBand = null;
                int? attackerFaction = null;

                foreach (var req in zoneProto.Requirements)
                {
                    req.Check(attackerBand, attackerFaction, ownerships, lastCaptureTimes, zonePrototypes, currentZoneId, frameTime);
                }
            }
        }
    }

    private void OnInit(Entity<WarZoneComponent> entity, ref ComponentInit args)
    {
        var ownership = _dbManager.GetStalkerWarOwnershipAsync(entity.Comp.ZoneProto).GetAwaiter().GetResult();

        if (ownership?.Band != null)
        {
            StalkerBand band = ownership.Band;
            ProtoId<STBandPrototype> bandProtoId = new(ownership.Band.BandProtoId);
            if (bandProtoId != default)
            {
                _dbManager.GetStalkerBandAsync(bandProtoId).GetAwaiter().GetResult();
            }
        }
    }
}
