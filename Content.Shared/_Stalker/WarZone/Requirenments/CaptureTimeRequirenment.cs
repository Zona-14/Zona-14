using System;
using Robust.Shared.Serialization;

namespace Content.Shared._Stalker.WarZone.Requirenments;

/// <summary>
/// Requirement: attacker must hold uncontested for a certain time.
/// </summary>
[Serializable, NetSerializable]
public sealed class CaptureTimeRequirenment : BaseWarZoneRequirenment
{
    [DataField("captureTime")]
    public float CaptureTime = 30f;

    /// <summary>
    /// Tracks current progress in seconds.
    /// </summary>
    [NonSerialized]
    public float ProgressSeconds = 0f;

    public override bool Check(IServerDbManager dbManager, Guid? attackerBand, Guid? attackerFaction, float frameTime)
    {
        ProgressSeconds += frameTime;
        return ProgressSeconds >= CaptureTime;
    }

    public void Reset()
    {
        ProgressSeconds = 0f;
    }
}
