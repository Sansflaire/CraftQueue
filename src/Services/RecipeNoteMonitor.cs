using System;

using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace CraftQueue.Services;

public sealed class RecipeNoteMonitor : IDisposable
{
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IPluginLog log;

    public bool IsCraftingLogOpen { get; private set; }
    public ushort SelectedRecipeId { get; private set; }

    public event Action? CraftingLogOpened;
    public event Action? CraftingLogClosed;
    public event Action<ushort>? SelectedRecipeChanged;

    public RecipeNoteMonitor(IAddonLifecycle addonLifecycle, IPluginLog log)
    {
        this.addonLifecycle = addonLifecycle;
        this.log = log;

        addonLifecycle.RegisterListener(AddonEvent.PostSetup, "RecipeNote", OnCraftingLogOpened);
        addonLifecycle.RegisterListener(AddonEvent.PreFinalize, "RecipeNote", OnCraftingLogClosed);
        addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "RecipeNote", OnCraftingLogUpdated);

        log.Info("RecipeNoteMonitor: Listening for RecipeNote addon events.");
    }

    private void OnCraftingLogOpened(AddonEvent type, AddonArgs args)
    {
        IsCraftingLogOpen = true;
        log.Debug("RecipeNoteMonitor: Crafting log opened.");
        CraftingLogOpened?.Invoke();
    }

    private void OnCraftingLogClosed(AddonEvent type, AddonArgs args)
    {
        IsCraftingLogOpen = false;
        SelectedRecipeId = 0;
        log.Debug("RecipeNoteMonitor: Crafting log closed.");
        CraftingLogClosed?.Invoke();
    }

    private void OnCraftingLogUpdated(AddonEvent type, AddonArgs args)
    {
        var recipeId = ReadSelectedRecipeId();
        if (recipeId != SelectedRecipeId)
        {
            SelectedRecipeId = recipeId;
            if (recipeId != 0)
            {
                log.Debug($"RecipeNoteMonitor: Selected recipe changed to {recipeId}.");
                SelectedRecipeChanged?.Invoke(recipeId);
            }
        }
    }

    /// <summary>
    /// Reads the selected recipe ID using the same approach Artisan uses:
    /// RecipeNote.Instance()->RecipeList->SelectedRecipe->RecipeId
    ///
    /// RecipeNote is the game's crafting notebook data structure.
    /// RecipeList (offset 0xB8) points to RecipeData which contains:
    ///   - Recipes (offset 0x00): pointer to array of RecipeEntry
    ///   - RecipesCount (offset 0x08): number of recipes in the array
    ///   - SelectedIndex (offset 0x458): index of the currently selected recipe
    /// Each RecipeEntry contains:
    ///   - RecipeId (offset 0x3B2): the Lumina recipe row ID
    /// </summary>
    private unsafe ushort ReadSelectedRecipeId()
    {
        try
        {
            var recipeNote = RecipeNote.Instance();
            if (recipeNote == null)
                return 0;

            // RecipeList is at offset 0xB8 from RecipeNote
            var recipeListPtr = *(byte**)((byte*)recipeNote + 0xB8);
            if (recipeListPtr == null)
                return 0;

            // Read RecipeData fields
            var recipes = *(byte**)(recipeListPtr + 0x00);       // Recipes array pointer
            var recipeCount = *(int*)(recipeListPtr + 0x08);     // RecipesCount
            var selectedIndex = *(ushort*)(recipeListPtr + 0x458); // SelectedIndex

            if (recipes == null || selectedIndex >= recipeCount || recipeCount <= 0)
                return 0;

            // Each RecipeEntry is 0x400 bytes. RecipeId is at offset 0x3B2 within each entry.
            var entryPtr = recipes + (selectedIndex * 0x400);
            var recipeId = *(ushort*)(entryPtr + 0x3B2);

            return recipeId;
        }
        catch (Exception ex)
        {
            log.Debug($"RecipeNoteMonitor: Failed to read recipe ID. {ex.Message}");
            return 0;
        }
    }

    public void Dispose()
    {
        addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "RecipeNote", OnCraftingLogOpened);
        addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "RecipeNote", OnCraftingLogClosed);
        addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "RecipeNote", OnCraftingLogUpdated);
    }
}
