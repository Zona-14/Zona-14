// SPDX-License-Identifier: MIT
// Ported from RMC-14 Content.Shared/_RMC14/Movement/RMCSetLastRealTickEvent.cs@2f5dc02e44.
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._Zona14.Movement;

[Serializable, NetSerializable]
public sealed class RMCSetLastRealTickEvent(GameTick tick) : EntityEventArgs
{
    public readonly GameTick Tick = tick;
}
