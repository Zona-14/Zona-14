using System;
using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Stalker.WarZone.Requirenments;

[Serializable, NetSerializable]
public sealed partial class CaptureTimeRequirenment : BaseWarZoneRequirenment
{
    [DataField("captureTime")]
    public float CaptureTime = 30f;

    [NonSerialized]
    public float ProgressSeconds = 0f;

    public override bool Check(
        int? attackerBand,
        int? attackerFaction,
        Dictionary<ProtoId<STWarZonePrototype>, (int? BandId, int? FactionId)> ownerships,
        Dictionary<ProtoId<STWarZonePrototype>, DateTime?> lastCaptureTimes,
        Dictionary<ProtoId<STWarZonePrototype>, STWarZonePrototype> zonePrototypes,
        ProtoId<STWarZonePrototype> currentZoneId,
        float frameTime)
    {
        ProgressSeconds += frameTime;
        return ProgressSeconds >= CaptureTime;
    }

    public void Reset()
    {
        ProgressSeconds = 0f;
    }
}