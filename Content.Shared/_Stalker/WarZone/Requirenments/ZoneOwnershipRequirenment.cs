using System;
using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Stalker.WarZone.Requirenments;

[Serializable, NetSerializable]
public sealed partial class ZoneOwnershipRequirenment : BaseWarZoneRequirenment
{
    [DataField("requiredZones")]
    public List<ProtoId<STWarZonePrototype>> RequiredZones = new();

    public override bool Check(
        int? attackerBand,
        int? attackerFaction,
        Dictionary<ProtoId<STWarZonePrototype>, (int? BandId, int? FactionId)> ownerships,
        Dictionary<ProtoId<STWarZonePrototype>, DateTime?> lastCaptureTimes,
        Dictionary<ProtoId<STWarZonePrototype>, STWarZonePrototype> zonePrototypes,
        ProtoId<STWarZonePrototype> currentZoneId,
        float frameTime)
    {
        foreach (var zoneId in RequiredZones)
        {
            if (!ownerships.TryGetValue(zoneId, out var owner))
                return false;

            var owns = false;
            if (attackerBand != null && owner.BandId == attackerBand)
                owns = true;
            if (attackerFaction != null && owner.FactionId == attackerFaction)
                owns = true;

            if (!owns)
                return false;
        }

        // Check capture cooldown for this zone
        if (lastCaptureTimes.TryGetValue(currentZoneId, out var lastCaptureTime) &&
            lastCaptureTime != null &&
            zonePrototypes.TryGetValue(currentZoneId, out var proto))
        {
            var cooldown = TimeSpan.FromHours(proto.CaptureCooldownHours);
            if (DateTime.UtcNow - lastCaptureTime < cooldown)
            {
                return false;
            }
        }

        return true;
    }
}