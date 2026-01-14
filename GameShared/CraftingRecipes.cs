namespace GameShared;

public static class CraftingRecipes
{
    public static readonly Dictionary<WeaponType, Dictionary<ResourceType, int>> Recipes = new()
    {
        { 
            WeaponType.WoodSword, new Dictionary<ResourceType, int> 
            {
                { ResourceType.Tree, 3 }
            }
        },
        { 
            WeaponType.StoneSword, new Dictionary<ResourceType, int> 
            {
                { ResourceType.Tree, 1 },
                { ResourceType.Rock, 2 }
            }
        },
        { 
            WeaponType.GoldSword, new Dictionary<ResourceType, int> 
            {
                { ResourceType.Tree, 1 },
                { ResourceType.GoldMine, 2 }
            }
        },
        { 
            WeaponType.Fence, new Dictionary<ResourceType, int> 
            {
                { ResourceType.Tree, 1 }
            }
        }
    };

    public static string GetRecipeString(WeaponType type)
    {
        if (!Recipes.TryGetValue(type, out var costs)) return "Unknown";
        
        var parts = new List<string>();
        foreach (var cost in costs)
        {
            parts.Add($"{cost.Value} {cost.Key}");
        }
        return string.Join(", ", parts);
    }
}
