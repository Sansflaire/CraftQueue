using System;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

using CraftQueue.Models;
using CraftQueue.Services;

namespace CraftQueue.Windows;

public sealed class MainWindow : IDisposable
{
    private readonly QueueManager queueManager;
    private readonly ArtisanIpcBridge artisan;
    private readonly RecipeNoteMonitor recipeMonitor;
    private readonly Configuration config;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IDataManager dataManager;
    private readonly ICondition condition;
    private readonly IChatGui chatGui;
    private readonly IPluginLog log;

    // Reference to settings window so we can toggle it from the gear button
    private readonly SettingsWindow settingsWindow;

    public bool IsVisible { get; set; }

    // State for the "Add from Crafting Log" section
    private int addQuantity = 1;

    // Cached info about the currently selected recipe in the crafting log
    private ushort cachedRecipeId;
    private string cachedItemName = string.Empty;
    private string cachedJobName = string.Empty;
    private int cachedRecipeLevel;

    // Artisan status (polled)
    private bool artisanBusy;
    private bool artisanRunning;
    private bool artisanPaused;

    // Crafting state
    private bool isCraftingAny;   // true when we've sent anything to Artisan
    private bool isCraftingList;  // true when running the full queue sequentially
    private Guid currentCraftingItemId;

    // Drag-and-drop state
    private Guid draggedItemId;

    // Favorites editing state
    private int editFavoriteQtyValue;

    public MainWindow(
        QueueManager queueManager,
        ArtisanIpcBridge artisan,
        RecipeNoteMonitor recipeMonitor,
        Configuration config,
        IDalamudPluginInterface pluginInterface,
        SettingsWindow settingsWindow,
        IDataManager dataManager,
        ICondition condition,
        IChatGui chatGui,
        IPluginLog log)
    {
        this.queueManager = queueManager;
        this.artisan = artisan;
        this.recipeMonitor = recipeMonitor;
        this.config = config;
        this.pluginInterface = pluginInterface;
        this.settingsWindow = settingsWindow;
        this.dataManager = dataManager;
        this.condition = condition;
        this.chatGui = chatGui;
        this.log = log;
    }

    public void UpdateArtisanStatus()
    {
        // Probe whether Artisan is actually responding (handles enable/disable at runtime)
        artisan.CheckAvailability();

        if (!artisan.ArtisanAvailable)
        {
            artisanBusy = false;
            artisanRunning = false;
            artisanPaused = false;
            return;
        }

        artisanBusy = artisan.IsBusy();
        artisanRunning = artisan.IsListRunning();
        artisanPaused = artisan.IsListPaused();
    }

    public void UpdateSelectedRecipe()
    {
        var recipeId = recipeMonitor.SelectedRecipeId;
        if (recipeId == cachedRecipeId || recipeId == 0)
            return;

        cachedRecipeId = recipeId;
        ResolveRecipeInfo(recipeId);
    }

    private void ResolveRecipeInfo(ushort recipeId)
    {
        try
        {
            var recipeSheet = dataManager.GetExcelSheet<Recipe>();
            var recipe = recipeSheet.GetRow(recipeId);
            var item = recipe.ItemResult.ValueNullable;
            cachedItemName = item?.Name.ToString() ?? "Unknown Item";

            var craftType = recipe.CraftType.ValueNullable;
            cachedJobName = craftType?.Name.ToString() ?? "???";

            var levelTable = recipe.RecipeLevelTable.ValueNullable;
            cachedRecipeLevel = levelTable != null ? (int)levelTable.Value.ClassJobLevel : 0;
        }
        catch (Exception ex)
        {
            cachedItemName = $"Recipe #{recipeId}";
            cachedJobName = "???";
            cachedRecipeLevel = 0;
            log.Debug($"MainWindow: Failed to resolve recipe {recipeId}. {ex.Message}");
        }
    }

    public string ResolveItemName(ushort recipeId)
    {
        try
        {
            var recipeSheet = dataManager.GetExcelSheet<Recipe>();
            var recipe = recipeSheet.GetRow(recipeId);
            return recipe.ItemResult.ValueNullable?.Name.ToString() ?? $"Recipe #{recipeId}";
        }
        catch
        {
            return $"Recipe #{recipeId}";
        }
    }

    public void Draw()
    {
        if (!IsVisible)
            return;

        ImGui.SetNextWindowSize(new Vector2(520, 560), ImGuiCond.FirstUseEver);

        var visible = IsVisible;
        if (ImGui.Begin("Craft Queue", ref visible, ImGuiWindowFlags.None))
        {
            if (!artisan.ArtisanAvailable)
            {
                DrawArtisanMissing();
            }
            else
            {
                DrawArtisanStatus();
                ImGui.Separator();
                DrawQueueList();
                ImGui.Separator();

                if (config.ShowFavorites && config.Favorites.Count > 0)
                {
                    DrawFavorites();
                    ImGui.Separator();
                }

                DrawAddFromCraftingLog();
            }
        }

        ImGui.End();
        IsVisible = visible;
    }

    private void DrawArtisanMissing()
    {
        ImGui.Spacing();
        ImGui.Spacing();

        // Yellow warning box
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.3f, 0.2f, 0.0f, 0.8f));
        ImGui.BeginChild("##artisan_warning", new Vector2(-1, 120), true);

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), "  !! ARTISAN NOT DETECTED !!");
        ImGui.Spacing();
        ImGui.TextWrapped("Craft Queue requires the Artisan plugin to function. Artisan handles the actual crafting — Craft Queue just manages your list.");
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Install Artisan, enable it, then reload this plugin.");

        ImGui.EndChild();
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.Spacing();
    }

    private void DrawArtisanStatus()
    {
        if (!config.ShowArtisanStatus)
            return;

        // Status text
        if (artisanPaused)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), "Artisan: Paused");
        }
        else if (artisanRunning || artisanBusy)
        {
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "Artisan: Crafting...");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Artisan: Idle");
        }

        // Control buttons on the right
        ImGui.SameLine(ImGui.GetWindowWidth() - 180);

        var canCraft = artisan.ArtisanAvailable && !artisanBusy && queueManager.Count > 0;
        if (!canCraft)
            ImGui.BeginDisabled();

        // Button label depends on AutoCraftEntireList setting
        var craftLabel = config.AutoCraftEntireList ? "Craft All" : "Craft Next";
        if (ImGui.Button(craftLabel))
            OnCraftButton();

        if (!canCraft)
            ImGui.EndDisabled();

        ImGui.SameLine();

        if (!artisanBusy)
            ImGui.BeginDisabled();

        if (ImGui.Button("Stop"))
        {
            StopCrafting();
            chatGui.Print("[CraftQueue] Stop requested.");
        }

        if (!artisanBusy)
            ImGui.EndDisabled();

        // Gear icon for settings
        ImGui.SameLine();
        if (ImGui.SmallButton("S"))
        {
            settingsWindow.IsVisible = !settingsWindow.IsVisible;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Settings");
        }
    }

    private void DrawQueueList()
    {
        var count = queueManager.Count;

        // Header
        ImGui.Text($"Queue ({count} item{(count != 1 ? "s" : "")})");
        ImGui.SameLine(ImGui.GetWindowWidth() - 80);

        if (count == 0)
            ImGui.BeginDisabled();

        if (ImGui.Button("Clear All"))
            queueManager.ClearQueue();

        if (count == 0)
            ImGui.EndDisabled();

        // Queue items
        if (count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "  Queue is empty. Add items from the Crafting Log below.");
            ImGui.Spacing();
            return;
        }

        if (!config.CompactMode)
            ImGui.Spacing();

        for (var i = 0; i < queueManager.Items.Count; i++)
        {
            var item = queueManager.Items[i];
            ImGui.PushID(item.Id.GetHashCode());
            DrawQueueItem(item, i);
            ImGui.PopID();
        }

        if (!config.CompactMode)
            ImGui.Spacing();
    }

    private void DrawQueueItem(QueueItem item, int index)
    {
        // Compact mode: reduce vertical spacing
        if (config.CompactMode)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 2));
        }

        // Status color
        var statusColor = item.Status switch
        {
            QueueItemStatus.Crafting => new Vector4(1.0f, 0.9f, 0.2f, 1.0f),
            QueueItemStatus.Completed => new Vector4(0.3f, 1.0f, 0.3f, 1.0f),
            QueueItemStatus.Failed => new Vector4(1.0f, 0.3f, 0.3f, 1.0f),
            _ => new Vector4(1.0f, 1.0f, 1.0f, 1.0f),
        };

        // Drag handle
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "=");

        // Drag source
        if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoHoldToOpenOthers))
        {
            draggedItemId = item.Id;
            ImGui.SetDragDropPayload("CQ_ITEM", ReadOnlySpan<byte>.Empty);
            ImGui.Text(item.ItemName);
            ImGui.EndDragDropSource();
        }

        // Drag target
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("CQ_ITEM");
                if (payload.Data != null)
                {
                    queueManager.MoveItem(draggedItemId, index);
                }
            }
            ImGui.EndDragDropTarget();
        }

        ImGui.SameLine();

        // Item name with status color
        ImGui.TextColored(statusColor, item.ItemName);

        if (item.Status == QueueItemStatus.Crafting)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.2f, 1.0f), "...");
        }

        // Quantity controls on the right side
        ImGui.SameLine(ImGui.GetWindowWidth() - 195);

        if (ImGui.SmallButton("-"))
        {
            queueManager.SetQuantity(item.Id, item.Quantity - 1);
        }

        ImGui.SameLine();
        ImGui.Text($"x{item.Quantity}");

        ImGui.SameLine();

        if (ImGui.SmallButton("+"))
        {
            queueManager.SetQuantity(item.Id, item.Quantity + 1);
        }

        // Craft single item button
        ImGui.SameLine();

        var canCraftSingle = artisan.ArtisanAvailable && !artisanBusy && item.Status == QueueItemStatus.Pending;
        if (!canCraftSingle)
            ImGui.BeginDisabled();

        if (ImGui.SmallButton(">"))
            OnCraftSingle(item);

        if (!canCraftSingle)
            ImGui.EndDisabled();

        // Materials button — colored if any HQ materials are set
        ImGui.SameLine();

        var hasHq = item.Materials.Length > 0 && item.Materials.Any(m => m.HqCount > 0);
        if (hasHq)
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.8f, 1.0f, 1.0f));

        if (ImGui.SmallButton("M"))
        {
            ImGui.OpenPopup($"mat_popup_{item.Id}");
        }

        if (hasHq)
            ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Materials");
        }

        DrawMaterialsPopup(item);

        // Remove button
        ImGui.SameLine();

        if (ImGui.SmallButton("X"))
        {
            queueManager.RemoveItem(item.Id);
        }

        if (config.CompactMode)
        {
            ImGui.PopStyleVar();
        }
    }

    private void DrawMaterialsPopup(QueueItem item)
    {
        if (ImGui.BeginPopup($"mat_popup_{item.Id}"))
        {
            ImGui.Text($"Materials for {item.ItemName}");
            ImGui.Separator();

            if (item.Materials.Length == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "No material data available.");
            }
            else
            {
                // Column headers
                ImGui.Text("Material");
                ImGui.SameLine(200);
                ImGui.Text("NQ");
                ImGui.SameLine(230);
                ImGui.Text("HQ");
                ImGui.Separator();

                for (var i = 0; i < item.Materials.Length; i++)
                {
                    var mat = item.Materials[i];
                    var total = mat.NqCount + mat.HqCount;
                    ImGui.PushID($"mat_{i}");

                    // Material name
                    ImGui.Text(mat.ItemName);
                    ImGui.SameLine(200);

                    // NQ count
                    ImGui.Text($"{mat.NqCount}");
                    ImGui.SameLine(230);

                    // HQ count
                    if (mat.HqCount > 0)
                        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"{mat.HqCount}");
                    else
                        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "0");

                    // +/- buttons to shift between NQ and HQ
                    ImGui.SameLine(260);

                    var canAddHq = mat.NqCount > 0;
                    if (!canAddHq) ImGui.BeginDisabled();
                    if (ImGui.SmallButton("+"))
                    {
                        mat.HqCount++;
                        mat.NqCount--;
                    }
                    if (!canAddHq) ImGui.EndDisabled();

                    ImGui.SameLine();

                    var canRemoveHq = mat.HqCount > 0;
                    if (!canRemoveHq) ImGui.BeginDisabled();
                    if (ImGui.SmallButton("-"))
                    {
                        mat.HqCount--;
                        mat.NqCount++;
                    }
                    if (!canRemoveHq) ImGui.EndDisabled();

                    ImGui.PopID();
                }
            }

            ImGui.Spacing();

            if (ImGui.Button("Close"))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawFavorites()
    {
        if (ImGui.CollapsingHeader($"Favorites ({config.Favorites.Count})###favorites_header"))
        {
            if (config.Favorites.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "  No favorites yet. Star a recipe to add it.");
            }
            else
            {
                // We may remove items during iteration, so track removal
                FavoriteRecipe? toRemove = null;

                for (var i = 0; i < config.Favorites.Count; i++)
                {
                    var fav = config.Favorites[i];
                    ImGui.PushID($"fav_{fav.RecipeId}");

                    // Star icon
                    ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.0f, 1.0f), "*");
                    ImGui.SameLine();

                    // Recipe name
                    ImGui.Text(fav.ItemName);
                    ImGui.SameLine();

                    // Quick-add button
                    if (ImGui.SmallButton($"Add x{fav.DefaultQuantity}"))
                    {
                        queueManager.AddItem(fav.RecipeId, fav.ItemName, fav.DefaultQuantity);
                        chatGui.Print($"[CraftQueue] Added {fav.DefaultQuantity}x {fav.ItemName} from favorites.");
                    }

                    ImGui.SameLine();

                    // Edit default quantity button
                    if (ImGui.SmallButton("Qty"))
                    {
                        ImGui.OpenPopup($"fav_qty_popup_{fav.RecipeId}");
                        editFavoriteQtyValue = fav.DefaultQuantity;
                    }

                    if (ImGui.BeginPopup($"fav_qty_popup_{fav.RecipeId}"))
                    {
                        ImGui.Text("Default Quantity:");
                        ImGui.SetNextItemWidth(100);
                        ImGui.InputInt("##favqty", ref editFavoriteQtyValue);
                        editFavoriteQtyValue = Math.Clamp(editFavoriteQtyValue, 1, 9999);

                        if (ImGui.Button("Set"))
                        {
                            fav.DefaultQuantity = editFavoriteQtyValue;
                            SaveConfig();
                            ImGui.CloseCurrentPopup();
                        }

                        ImGui.SameLine();

                        if (ImGui.Button("Remove"))
                        {
                            toRemove = fav;
                            ImGui.CloseCurrentPopup();
                        }

                        ImGui.EndPopup();
                    }

                    ImGui.PopID();
                }

                if (toRemove != null)
                {
                    config.Favorites.Remove(toRemove);
                    SaveConfig();
                }
            }

            ImGui.Spacing();
        }
    }

    private void DrawAddFromCraftingLog()
    {
        ImGui.Text("Add from Crafting Log");
        ImGui.Spacing();

        if (!recipeMonitor.IsCraftingLogOpen)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "  Open the Crafting Log to add items.");
            return;
        }

        if (cachedRecipeId == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "  Select a recipe in the Crafting Log.");
            return;
        }

        // Show selected recipe info
        ImGui.Text($"  Selected: {cachedItemName}");
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), $"({cachedJobName} Lv.{cachedRecipeLevel})");

        // Quantity input
        ImGui.Text("  Quantity:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        ImGui.InputInt("##addqty", ref addQuantity);
        addQuantity = Math.Clamp(addQuantity, 1, 9999);

        ImGui.SameLine();

        // Add button
        if (ImGui.Button("Add to Queue"))
        {
            var name = cachedItemName;
            if (string.IsNullOrEmpty(name) || name.StartsWith("Recipe #"))
                name = ResolveItemName(cachedRecipeId);

            var materials = ReadRecipeMaterials(cachedRecipeId);
            queueManager.AddItem(cachedRecipeId, name, addQuantity, materials);
            chatGui.Print($"[CraftQueue] Added {addQuantity}x {name} to queue.");
            log.Info($"MainWindow: Added {addQuantity}x {name} (recipe {cachedRecipeId}) to queue.");
        }

        ImGui.SameLine();

        // Favorite button
        var isFav = IsFavorite(cachedRecipeId);
        if (isFav)
        {
            if (ImGui.Button("Unfavorite"))
            {
                RemoveFavorite(cachedRecipeId);
            }
        }
        else
        {
            if (ImGui.Button("Favorite"))
            {
                AddFavorite(cachedRecipeId, cachedItemName, addQuantity);
            }
        }
    }

    // ─── Material reading ─────────────────────────────────────────────

    /// <summary>
    /// Reads a recipe's ingredient list from Lumina and returns MaterialPreference[]
    /// with all materials defaulting to NQ.
    /// </summary>
    private MaterialPreference[] ReadRecipeMaterials(ushort recipeId)
    {
        try
        {
            var recipeSheet = dataManager.GetExcelSheet<Recipe>();
            var recipe = recipeSheet.GetRow(recipeId);
            var materials = new System.Collections.Generic.List<MaterialPreference>();

            foreach (var ing in recipe.Ingredient)
            {
                var item = ing.ValueNullable;
                if (item == null || item.Value.RowId == 0)
                    continue;

                // Find the amount for this ingredient index
                // We'll match by position in the Ingredient collection
                materials.Add(new MaterialPreference
                {
                    ItemId = item.Value.RowId,
                    ItemName = item.Value.Name.ToString(),
                    NqCount = 0, // Will be filled with amount below
                    HqCount = 0,
                });
            }

            // Read amounts separately
            for (var i = 0; i < materials.Count && i < recipe.AmountIngredient.Count; i++)
            {
                materials[i].NqCount = recipe.AmountIngredient[i];
            }

            // Remove entries with zero amount
            materials.RemoveAll(m => m.NqCount <= 0);

            return materials.ToArray();
        }
        catch (Exception ex)
        {
            log.Debug($"MainWindow: Failed to read materials for recipe {recipeId}. {ex.Message}");
            return Array.Empty<MaterialPreference>();
        }
    }

    // ─── Favorites helpers ──────────────────────────────────────────────

    private bool IsFavorite(ushort recipeId)
    {
        return config.Favorites.Any(f => f.RecipeId == recipeId);
    }

    private void AddFavorite(ushort recipeId, string itemName, int defaultQuantity)
    {
        if (IsFavorite(recipeId))
            return;

        config.Favorites.Add(new FavoriteRecipe
        {
            RecipeId = recipeId,
            ItemName = itemName,
            DefaultQuantity = Math.Clamp(defaultQuantity, 1, 9999),
            DefaultNqOnly = true,
        });

        SaveConfig();
        chatGui.Print($"[CraftQueue] Added {itemName} to favorites.");
    }

    private void RemoveFavorite(ushort recipeId)
    {
        var fav = config.Favorites.FirstOrDefault(f => f.RecipeId == recipeId);
        if (fav != null)
        {
            config.Favorites.Remove(fav);
            SaveConfig();
            chatGui.Print($"[CraftQueue] Removed {fav.ItemName} from favorites.");
        }
    }

    private void SaveConfig()
    {
        pluginInterface.SavePluginConfig(config);
    }

    // ─── Crafting logic ─────────────────────────────────────────────────

    /// <summary>
    /// Stops CraftQueue from sending any more items to Artisan.
    /// Does NOT interfere with Artisan — it can finish its current craft.
    /// </summary>
    public void StopCrafting()
    {
        isCraftingAny = false;
        isCraftingList = false;
    }

    private void OnCraftSingle(QueueItem item)
    {
        if (artisanBusy)
        {
            chatGui.PrintError("[CraftQueue] Artisan is busy! Wait for it to finish.");
            return;
        }

        queueManager.SetStatus(item.Id, QueueItemStatus.Crafting);
        currentCraftingItemId = item.Id;
        isCraftingList = false; // single item — don't advance to next
        isCraftingAny = true;   // but do track completion
        artisan.CraftItem(item.RecipeId, item.Quantity);
        chatGui.Print($"[CraftQueue] Sending {item.Quantity}x {item.ItemName} to Artisan.");
    }

    /// <summary>
    /// Called when the main craft button is pressed.
    /// If AutoCraftEntireList is on, crafts all items sequentially.
    /// If off, crafts only the first pending item (Craft Next).
    /// </summary>
    private void OnCraftButton()
    {
        if (artisanBusy)
        {
            chatGui.PrintError("[CraftQueue] Artisan is busy! Wait for it to finish.");
            return;
        }

        var first = queueManager.GetFirstPending();
        if (first == null)
        {
            chatGui.Print("[CraftQueue] No pending items in queue.");
            return;
        }

        isCraftingList = config.AutoCraftEntireList;
        isCraftingAny = true;
        queueManager.SetStatus(first.Id, QueueItemStatus.Crafting);
        currentCraftingItemId = first.Id;
        artisan.CraftItem(first.RecipeId, first.Quantity);

        if (config.AutoCraftEntireList)
            chatGui.Print($"[CraftQueue] Starting queue: {first.Quantity}x {first.ItemName}");
        else
            chatGui.Print($"[CraftQueue] Crafting next: {first.Quantity}x {first.ItemName}");
    }

    /// <summary>
    /// Called from the Framework.Update polling loop to track completion
    /// and advance the queue when running in list mode.
    /// </summary>
    public void UpdateCraftingProgress()
    {
        if (!isCraftingAny || !artisan.ArtisanAvailable)
            return;

        // If Artisan is no longer busy, the current item is done
        if (!artisanBusy && !artisanRunning)
        {
            var current = queueManager.GetById(currentCraftingItemId);
            if (current != null && current.Status == QueueItemStatus.Crafting)
            {
                queueManager.SetStatus(current.Id, QueueItemStatus.Completed);

                if (config.AutoRemoveCompleted)
                    queueManager.RemoveItem(current.Id);
            }

            // If we're running the full list, advance to next
            if (isCraftingList)
            {
                var next = queueManager.GetFirstPending();
                if (next != null)
                {
                    queueManager.SetStatus(next.Id, QueueItemStatus.Crafting);
                    currentCraftingItemId = next.Id;
                    artisan.CraftItem(next.RecipeId, next.Quantity);
                    chatGui.Print($"[CraftQueue] Next: {next.Quantity}x {next.ItemName}");
                    return; // keep isCraftingAny true
                }
                else
                {
                    chatGui.Print("[CraftQueue] Queue complete!");
                }
            }

            // Done — reset state
            isCraftingAny = false;
            isCraftingList = false;
        }
    }

    public void Dispose()
    {
        // No resources to clean up
    }
}
