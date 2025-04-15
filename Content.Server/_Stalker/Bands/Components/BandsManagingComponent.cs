using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using System; // For Enum

namespace Content.Server._Stalker.Bands.Components
{
    /// <summary>
    /// Component attached to entities that can open the Band Management UI.
    /// </summary>
    [RegisterComponent, NetworkedComponent]
    public sealed partial class BandsManagingComponent : Component
    {
        /// <summary>
        /// The BUI key used for this component.
        /// </summary>
        [DataField("uiKey", customTypeSerializer: typeof(EnumSerializer))]
        public Enum UiKey = BandsManagingUiKey.Key;
    }

    /// <summary>
    /// Enum for the BUI key.
    /// </summary>
    [Serializable, NetSerializable]
    public enum BandsManagingUiKey : byte
    {
        Key
    }
}