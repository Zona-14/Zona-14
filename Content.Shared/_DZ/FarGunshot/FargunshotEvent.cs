using Robust.Shared.Serialization;

namespace Content.Shared._DZ.FarGunshot;

[Serializable, NetSerializable]
public sealed class FargunshotEvent : EntityEventArgs
{
    public readonly EntityUid gunUid;

    public FargunshotEvent(EntityUid gunUid)
    {
        this.gunUid = gunUid;
    }
}
