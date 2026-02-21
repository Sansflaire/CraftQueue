using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
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
    private readonly IDataManager dataManager;
    private readonly ICondition condition;
    private readonly IChatGui chatGui;
    private readonly IPluginLog log;

    public bool IsVisible { get; set; }

    // State for the "Add from Crafting Log" section
    private int addQuantity = 1;
    private bool addUseHq = false;

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

    public MainWindow(
        QueueManager queueManager,
        ArtisanIpcBridge artisan,
        RecipeNoteMonitor recipeMonitor,
        Configuration config,
        IDataManager dataManager,
        ICondition condition,
        IChatGui chatGui,
        IPluginLog log)
    {
        this.queueManager = queueManager;
        this.artisan = artisan;
        this.recipeMonitor = recipeMonitor;
        this.config = config;
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

        ImGui.SetNextWindowSize(new Vector2(520, 480), ImGuiCond.FirstUseEver);

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
        if (!artisan.ArtisanAvailable)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f), "Artisan: Not Available");
            if (config.WarnIfArtisanMissing)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "(Install Artisan to craft)");
            }
        }
        else if (artisanPaused)
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

        // Control buttons on the same line
        ImGui.SameLine(ImGui.GetWindowWidth() - 200);

        var canCraft = artisan.ArtisanAvailable && !artisanBusy && queueManager.Count > 0;
        if (!canCraft)
            ImGui.BeginDisabled();

        if (ImGui.Button("Craft All"))
            OnCraftAll();

        if (!canCraft)
            ImGui.EndDisabled();

        ImGui.SameLine();

        if (!artisanBusy)
            ImGui.BeginDisabled();

        if (artisanPaused)
        {
            if (ImGui.Button("Resume"))
                artisan.SetListPause(false);
        }
        else
        {
            if (ImGui.Button("Pause"))
                artisan.SetListPause(true);
        }

        ImGui.SameLine();

        if (ImGui.Button("Stop"))
        {
            artisan.SetStopRequest(true);
            isCraftingAny = false;
            isCraftingList = false;
        }

        if (!artisanBusy)
            ImGui.EndDisabled();
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

        ImGui.Spacing();

        for (var i = 0; i < queueManager.Items.Count; i++)
        {
            var item = queueManager.Items[i];
            ImGui.PushID(item.Id.GetHashCode());
            DrawQueueItem(item, i);
            ImGui.PopID();
        }

        ImGui.Spacing();
    }

    private void DrawQueueItem(QueueItem item, int index)
    {
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
        ImGui.SameLine(ImGui.GetWindowWidth() - 170);

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

        // Remove button
        ImGui.SameLine();

        if (ImGui.SmallButton("X"))
        {
            queueManager.RemoveItem(item.Id);
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

            queueManager.AddItem(cachedRecipeId, name, addQuantity, addUseHq);
            chatGui.Print($"[CraftQueue] Added {addQuantity}x {name} to queue.");
            log.Info($"MainWindow: Added {addQuantity}x {name} (recipe {cachedRecipeId}) to queue.");
        }
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

    private void OnCraftAll()
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

        isCraftingList = true;
        isCraftingAny = true;
        queueManager.SetStatus(first.Id, QueueItemStatus.Crafting);
        currentCraftingItemId = first.Id;
        artisan.CraftItem(first.RecipeId, first.Quantity);
        chatGui.Print($"[CraftQueue] Starting queue: {first.Quantity}x {first.ItemName}");
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
