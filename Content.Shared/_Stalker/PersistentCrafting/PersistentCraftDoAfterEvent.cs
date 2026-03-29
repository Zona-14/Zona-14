using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Stalker.PersistentCrafting;

[Serializable, NetSerializable]
public sealed partial class PersistentCraftDoAfterEvent : DoAfterEvent
{
    public string RecipeId { get; }
    public int RemainingCount { get; }
    public int RequestedCount { get; }

    public PersistentCraftDoAfterEvent(string recipeId, int remainingCount = 1, int requestedCount = 1)
    {
        RecipeId = recipeId;
        RemainingCount = remainingCount;
        RequestedCount = requestedCount;
    }

    public override DoAfterEvent Clone()
    {
        return new PersistentCraftDoAfterEvent(RecipeId, RemainingCount, RequestedCount);
    }
}
