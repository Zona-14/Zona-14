using System;
using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Stalker.WarZone.Requirenments;

/// <summary>
/// Requirement: attacker must own all specified zones.
/// </summary>
[Serializable, NetSerializable]
public sealed class ZoneOwnershipRequirenment : BaseWarZoneRequirenment
{
    [DataField("requiredZones")]
    public List<ProtoId<STWarZonePrototype>> RequiredZones = new();

    public override bool Check(IServerDbManager dbManager, Guid? attackerBand, Guid? attackerFaction, float frameTime)
    {
        foreach (var zoneId in RequiredZones)
        {
            var ownership = dbManager.GetStalkerWarOwnershipAsync(zoneId).Result;
            if (ownership == null)
                return false;

            var owns = false;
            if (attackerBand != null && ownership.BandId == attackerBand)
                owns = true;
            if (attackerFaction != null && ownership.FactionId == attackerFaction)
                owns = true;

            if (!owns)
                return false;
        }

        return true;
    }
}
