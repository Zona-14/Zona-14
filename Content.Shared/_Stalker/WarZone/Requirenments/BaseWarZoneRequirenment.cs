using System;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Stalker.WarZone.Requirenments;

/// <summary>
/// Base class for all War Zone requirements.
/// </summary>
[Serializable, NetSerializable]
public abstract class BaseWarZoneRequirenment
{
    /// <summary>
    /// Checks if the requirement is met for the given attacker.
    /// </summary>
    /// <param name="dbManager">Database manager for ownership queries.</param>
    /// <param name="attackerBand">Attacking Band ID (nullable).</param>
    /// <param name="attackerFaction">Attacking Faction ID (nullable).</param>
    /// <param name="frameTime">Frame time delta in seconds.</param>
    /// <returns>True if requirement is met, false otherwise.</returns>
    public abstract bool Check(IServerDbManager dbManager, Guid? attackerBand, Guid? attackerFaction, float frameTime);
}
