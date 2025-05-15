using Content.Shared.Tools.Components;
using Robust.Shared.Audio;

namespace Content.Server._DZ.FarGunshot;

[RegisterComponent]
public sealed partial class FarGunshotComponent : Component
{
    /// <summary>
    /// The start max range of farsound, non modified
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Range = 260;

    /// <summary>
    /// The base sound to use when the gun is fired from far dist.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/_DZ/Weapons/Guns/Gunshots/gunshot-far.ogg");

    // TODO: silencer impl
}
