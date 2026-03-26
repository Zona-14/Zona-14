using System.Linq;

namespace Content.Shared._Stalker.PersistentCrafting;

public static class PersistentCraftingHelper
{
    private static readonly BranchDefinition[] BranchDefinitions =
    {
        new(PersistentCraftBranch.Weapon, "persistent-craft-branch-weapon"),
        new(PersistentCraftBranch.Armor, "persistent-craft-branch-armor"),
        new(PersistentCraftBranch.Anomaly, "persistent-craft-branch-anomaly"),
    };

    private static readonly IReadOnlyList<PersistentCraftBranch> Branches = BranchDefinitions
        .Select(definition => definition.Branch)
        .ToArray();

    public static IReadOnlyList<PersistentCraftBranch> EnumerateBranches()
    {
        return Branches;
    }

    public static int GetBranchCount()
    {
        return BranchDefinitions.Length;
    }

    public static int GetBranchIndex(PersistentCraftBranch branch)
    {
        for (var i = 0; i < BranchDefinitions.Length; i++)
        {
            if (BranchDefinitions[i].Branch == branch)
                return i;
        }

        return 0;
    }

    public static PersistentCraftBranch GetBranchByIndex(int index)
    {
        if (index >= 0 && index < BranchDefinitions.Length)
            return BranchDefinitions[index].Branch;

        return BranchDefinitions[0].Branch;
    }

    public static string GetBranchLocKey(PersistentCraftBranch branch)
    {
        return BranchDefinitions[GetBranchIndex(branch)].LocKey;
    }

    public static string? GetDisplayPrototypeId(PersistentCraftRecipePrototype recipe)
    {
        if (!string.IsNullOrWhiteSpace(recipe.DisplayProto))
            return recipe.DisplayProto;

        return recipe.Results.FirstOrDefault()?.Proto;
    }

    public static int GetPointReward(PersistentCraftRecipePrototype recipe)
    {
        if (recipe.PointReward > 0)
            return recipe.PointReward;

        return 1;
    }

    public static string GetTierDisplayLabel(int tier)
    {
        return tier switch
        {
            1 => "I",
            2 => "II",
            3 => "III",
            4 => "IV",
            5 => "V",
            _ => tier.ToString(),
        };
    }

    private sealed class BranchDefinition
    {
        public PersistentCraftBranch Branch { get; }
        public string LocKey { get; }

        public BranchDefinition(PersistentCraftBranch branch, string locKey)
        {
            Branch = branch;
            LocKey = locKey;
        }
    }

}
