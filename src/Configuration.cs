using System;
using System.Collections.Generic;

using Dalamud.Configuration;

namespace CraftQueue;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

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

    // Favorites
    public List<FavoriteRecipe> Favorites { get; set; } = new();
}

[Serializable]
public sealed class FavoriteRecipe
{
    public ushort RecipeId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int DefaultQuantity { get; set; } = 1;
    public bool DefaultNqOnly { get; set; } = true;
}
