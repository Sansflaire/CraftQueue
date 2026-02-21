using System;
using System.Collections.Generic;
using System.Linq;

using Dalamud.Configuration;

using CraftQueue.Models;

namespace CraftQueue;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    // Crafting behavior
    public bool AutoCraftEntireList { get; set; } = true;

    // Window behavior
    public bool AutoOpenWithCraftingLog { get; set; } = true;
    public bool AutoCloseWithCraftingLog { get; set; } = false;
    public bool ShowFavorites { get; set; } = true;
    public bool CompactMode { get; set; } = false;

    // Queue behavior
    public bool AutoRemoveCompleted { get; set; } = true;
    public bool ConfirmBeforeRemoving { get; set; } = false;

    // Artisan integration
    public bool ShowArtisanStatus { get; set; } = true;
    public int PollingIntervalMs { get; set; } = 500;

    // Favorites (legacy — kept for migration from v1 configs)
    public List<FavoriteRecipe> Favorites { get; set; } = new();

    // Favorites (grouped — v2+)
    public List<FavoriteGroup> FavoriteGroups { get; set; } = new();

    /// <summary>
    /// Migrates v1 flat favorites into grouped favorites, or ensures
    /// at least one default group exists for fresh configs.
    /// </summary>
    public void MigrateIfNeeded()
    {
        if (Favorites.Count > 0 && FavoriteGroups.Count == 0)
        {
            // Migrate old flat favorites into a single group
            FavoriteGroups.Add(new FavoriteGroup
            {
                Name = "Uncategorized",
                IsCollapsed = false,
                Recipes = new List<FavoriteRecipe>(Favorites),
            });
            Favorites.Clear();
            Version = 2;
        }
        else if (FavoriteGroups.Count == 0)
        {
            // Fresh config — create one default group
            FavoriteGroups.Add(new FavoriteGroup
            {
                Name = "General",
                IsCollapsed = false,
                Recipes = new(),
            });
            Version = 2;
        }
    }

    /// <summary>
    /// Returns true if any group contains a favorite with the given recipe ID.
    /// </summary>
    public bool IsFavorite(ushort recipeId)
    {
        foreach (var group in FavoriteGroups)
        {
            if (group.Recipes.Any(f => f.RecipeId == recipeId))
                return true;
        }
        return false;
    }
}

[Serializable]
public sealed class FavoriteGroup
{
    public string Name { get; set; } = "General";
    public bool IsCollapsed { get; set; } = false;
    public List<FavoriteRecipe> Recipes { get; set; } = new();
}

[Serializable]
public sealed class FavoriteRecipe
{
    public ushort RecipeId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int DefaultQuantity { get; set; } = 1;
    public List<MaterialPreference> Materials { get; set; } = new();
}
