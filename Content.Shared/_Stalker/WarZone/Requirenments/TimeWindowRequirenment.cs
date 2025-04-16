using System;
using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.IoC; // Keep for DataDefinition if needed elsewhere, or remove if unused

namespace Content.Shared._Stalker.WarZone.Requirenments;

[Serializable, NetSerializable]
public sealed partial class TimeWindowRequirenment : BaseWarZoneRequirenment
{
    [DataField("startHourUtc", required: true)]
    public int StartHourUtc { get; private set; }

    [DataField("endHourUtc", required: true)]
    public int EndHourUtc { get; private set; }

    public override CaptureBlockReason Check(
        string? attackerBandProtoId,
        string? attackerFactionProtoId,
        Dictionary<ProtoId<STWarZonePrototype>, (string? BandProtoId, string? FactionProtoId)> ownerships,
        Dictionary<ProtoId<STWarZonePrototype>, DateTime?> lastCaptureTimes,
        Dictionary<ProtoId<STWarZonePrototype>, STWarZonePrototype> zonePrototypes,
        ProtoId<STWarZonePrototype> currentZoneId,
        float frameTime)
    {
        var currentUtcTime = DateTime.UtcNow;
        var currentHour = currentUtcTime.Hour;

        // Basic validation (optional, could rely on prototype validation)
        if (StartHourUtc < 0 || StartHourUtc > 23 || EndHourUtc < 0 || EndHourUtc > 23)
        {
             // Consider logging an error here if RobustToolbox logging is available in Shared
             // Logger.ErrorS("warzone", $"Invalid UTC hours in TimeWindowRequirement for zone {currentZoneId}. Start: {StartHourUtc}, End: {EndHourUtc}");
             return CaptureBlockReason.TimeWindow; // Or a specific Error reason
        }

        // Handle time ranges that span across midnight
        if (StartHourUtc <= EndHourUtc)
        {
            // Standard range (e.g., 8 to 16)
            if (currentHour >= StartHourUtc && currentHour < EndHourUtc)
            {
                return CaptureBlockReason.None;
            }
        }
        else // StartHourUtc > EndHourUtc
        {
            // Range spans midnight (e.g., 22 to 4)
            if (currentHour >= StartHourUtc || currentHour < EndHourUtc)
            {
                return CaptureBlockReason.None;
            }
        }

        // If we reach here, the time is outside the allowed window
        return CaptureBlockReason.TimeWindow; // Need to define this enum value
    }
}
