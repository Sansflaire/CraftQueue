using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

using CraftQueue.Models;
using CraftQueue.Services;

namespace CraftQueue.Windows;

public sealed class FavoritesWindow : IDisposable
{
    private readonly Configuration config;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly QueueManager queueManager;
    private readonly IDataManager dataManager;
    private readonly IChatGui chatGui;
    private readonly IPluginLog log;

    public bool IsVisible { get; set; }

    // Drag-and-drop state
    private int draggedGroupIndex = -1;   // group index of item being dragged (CQ_FAV)
    private int draggedRecipeIndex = -1;  // recipe index of item being dragged (CQ_FAV)
    private int draggedGroupForReorder = -1; // group index being reordered (CQ_GROUP)
    private static readonly byte[] DragPayloadData = { 1 };

    // Editing state
    private int editFavoriteQtyValue;
    private string newGroupName = string.Empty;
    private string renamingGroupName = string.Empty;

    public FavoritesWindow(
        Configuration config,
        IDalamudPluginInterface pluginInterface,
        QueueManager queueManager,
        IDataManager dataManager,
        IChatGui chatGui,
        IPluginLog log)
    {
        this.config = config;
        this.pluginInterface = pluginInterface;
        this.queueManager = queueManager;
        this.dataManager = dataManager;
        this.chatGui = chatGui;
        this.log = log;
    }

    public void Draw()
    {
        if (!IsVisible)
            return;

        ImGui.SetNextWindowSize(new Vector2(480, 500), ImGuiCond.FirstUseEver);

        var visible = IsVisible;
        if (ImGui.Begin("Craft Queue - Favorites", ref visible, ImGuiWindowFlags.None))
        {
            DrawToolbar();
            ImGui.Separator();
            DrawGroups();
        }

        ImGui.End();
        IsVisible = visible;
    }

    // ─── Toolbar ────────────────────────────────────────────────────────

    private void DrawToolbar()
    {
        var totalCount = 0;
        foreach (var g in config.FavoriteGroups)
            totalCount += g.Recipes.Count;

        ImGui.Text($"Favorites ({totalCount})");

        ImGui.SameLine(ImGui.GetWindowWidth() - 105);
        if (ImGui.Button("+ New Group"))
        {
            ImGui.OpenPopup("new_group_popup");
            newGroupName = string.Empty;
        }

        if (ImGui.BeginPopup("new_group_popup"))
        {
            ImGui.Text("Group Name:");
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("##newgroupname", ref newGroupName, 64);

            if (ImGui.Button("Create") && !string.IsNullOrWhiteSpace(newGroupName))
            {
                config.FavoriteGroups.Add(new FavoriteGroup
                {
                    Name = newGroupName.Trim(),
                    IsCollapsed = false,
                    Recipes = new(),
                });
                SaveConfig();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }

    // ─── Groups ─────────────────────────────────────────────────────────

    private void DrawGroups()
    {
        FavoriteGroup? groupToDelete = null;

        for (var gi = 0; gi < config.FavoriteGroups.Count; gi++)
        {
            var group = config.FavoriteGroups[gi];
            ImGui.PushID($"group_{gi}");

            // Manual header row: ArrowButton + Selectable label + R + X as separate widgets.
            // ArrowButton: collapse toggle (click), group reorder drag SOURCE, group reorder drop TARGET.
            // Selectable: item drop target (CQ_FAV) only — separate widget, separate payload type.
            var arrowDir = group.IsCollapsed ? ImGuiDir.Right : ImGuiDir.Down;
            if (ImGui.ArrowButton($"##arrow_{gi}", arrowDir))
            {
                group.IsCollapsed = !group.IsCollapsed;
            }

            // Group reorder drag source on the arrow button
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoHoldToOpenOthers))
            {
                draggedGroupForReorder = gi;
                ImGui.SetDragDropPayload("CQ_GROUP", DragPayloadData);
                ImGui.Text($"Group: {group.Name}");
                ImGui.EndDragDropSource();
            }

            // Group reorder drop target also on the arrow button (dedicated widget, dedicated payload)
            if (ImGui.BeginDragDropTarget())
            {
                unsafe
                {
                    var groupPayload = ImGui.AcceptDragDropPayload("CQ_GROUP");
                    if (groupPayload.Data != null && draggedGroupForReorder >= 0 && draggedGroupForReorder != gi)
                    {
                        var from = draggedGroupForReorder;
                        var to = gi;
                        var moved = config.FavoriteGroups[from];
                        config.FavoriteGroups.RemoveAt(from);
                        config.FavoriteGroups.Insert(to, moved);
                        SaveConfig();
                        draggedGroupForReorder = -1;
                    }
                }
                ImGui.EndDragDropTarget();
            }

            ImGui.SameLine();

            // Selectable label — drop target for item moves (CQ_FAV) only.
            var buttonsWidth = 60f;
            var labelWidth = ImGui.GetWindowWidth() - ImGui.GetCursorPosX() - buttonsWidth - ImGui.GetStyle().WindowPadding.X;
            ImGui.Selectable($"{group.Name} ({group.Recipes.Count})###group_label_{gi}", false, ImGuiSelectableFlags.None, new Vector2(labelWidth, 0));

            // Click toggles collapse
            if (ImGui.IsItemClicked())
            {
                group.IsCollapsed = !group.IsCollapsed;
            }

            if (ImGui.BeginDragDropTarget())
            {
                unsafe
                {
                    var favPayload = ImGui.AcceptDragDropPayload("CQ_FAV");
                    if (favPayload.Data != null && draggedGroupIndex >= 0 && draggedRecipeIndex >= 0)
                    {
                        var sourceGroup = config.FavoriteGroups[draggedGroupIndex];
                        if (draggedRecipeIndex < sourceGroup.Recipes.Count)
                        {
                            var fav = sourceGroup.Recipes[draggedRecipeIndex];
                            sourceGroup.Recipes.RemoveAt(draggedRecipeIndex);
                            group.Recipes.Add(fav);
                            SaveConfig();
                        }
                        draggedGroupIndex = -1;
                        draggedRecipeIndex = -1;
                    }
                }
                ImGui.EndDragDropTarget();
            }

            // Right-aligned Rename and Delete buttons
            ImGui.SameLine(ImGui.GetWindowWidth() - 55);

            if (ImGui.SmallButton("R"))
            {
                renamingGroupName = group.Name;
                ImGui.OpenPopup($"rename_group_popup_{gi}");
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Rename group");

            ImGui.SameLine();

            // Cannot delete the last remaining group
            var canDelete = config.FavoriteGroups.Count > 1;
            if (!canDelete)
                ImGui.BeginDisabled();

            if (ImGui.SmallButton("X"))
            {
                groupToDelete = group;
            }

            if (!canDelete)
                ImGui.EndDisabled();

            if (ImGui.IsItemHovered())
            {
                if (config.FavoriteGroups.Count <= 1)
                    ImGui.SetTooltip("Cannot delete the last group");
                else if (group.Recipes.Count > 0)
                    ImGui.SetTooltip("Delete group (items move to first group)");
                else
                    ImGui.SetTooltip("Delete empty group");
            }

            // Separator under each group header for visual clarity
            ImGui.Separator();

            // Rename popup
            DrawRenameGroupPopup(gi, group);

            // Draw favorites if expanded
            var isOpen = !group.IsCollapsed;
            if (isOpen)
            {
                ImGui.Indent(12);
                DrawGroupRecipes(gi, group);
                ImGui.Unindent(12);
            }

            ImGui.PopID();
        }

        // Process deferred deletion — move any remaining items to the first other group
        if (groupToDelete != null)
        {
            if (groupToDelete.Recipes.Count > 0)
            {
                var targetGroup = config.FavoriteGroups.First(g => g != groupToDelete);
                targetGroup.Recipes.AddRange(groupToDelete.Recipes);
            }
            config.FavoriteGroups.Remove(groupToDelete);
            SaveConfig();
        }
    }

    private void DrawRenameGroupPopup(int gi, FavoriteGroup group)
    {
        if (ImGui.BeginPopup($"rename_group_popup_{gi}"))
        {
            ImGui.Text("Rename Group:");
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("##renamegroupinput", ref renamingGroupName, 64);

            if (ImGui.Button("OK") && !string.IsNullOrWhiteSpace(renamingGroupName))
            {
                group.Name = renamingGroupName.Trim();
                SaveConfig();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }

    // ─── Per-group recipe list with drag-and-drop ───────────────────────

    private void DrawGroupRecipes(int groupIndex, FavoriteGroup group)
    {
        FavoriteRecipe? toRemove = null;

        for (var ri = 0; ri < group.Recipes.Count; ri++)
        {
            var fav = group.Recipes[ri];
            ImGui.PushID($"fav_{groupIndex}_{fav.RecipeId}");

            // Drag handle — drag source for cross-group moves
            ImGui.SmallButton("=");

            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoHoldToOpenOthers))
            {
                draggedGroupIndex = groupIndex;
                draggedRecipeIndex = ri;
                ImGui.SetDragDropPayload("CQ_FAV", DragPayloadData);
                ImGui.Text(fav.ItemName);
                ImGui.EndDragDropSource();
            }

            ImGui.SameLine();

            // Star icon
            ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.0f, 1.0f), "*");
            ImGui.SameLine();

            // Recipe name as a Selectable — acts as the drop target for reordering.
            // Same pattern as the group header Selectables.
            var buttonsWidth = 170f;
            var nameWidth = ImGui.GetWindowWidth() - ImGui.GetCursorPosX() - buttonsWidth - ImGui.GetStyle().WindowPadding.X;
            ImGui.Selectable($"{fav.ItemName}###fav_name_{ri}", false, ImGuiSelectableFlags.None, new Vector2(nameWidth, 0));

            // Drop target on the name — reorder within group or cross-group move
            if (ImGui.BeginDragDropTarget())
            {
                unsafe
                {
                    var payload = ImGui.AcceptDragDropPayload("CQ_FAV");
                    if (payload.Data != null && draggedGroupIndex >= 0 && draggedRecipeIndex >= 0)
                    {
                        var sourceGroup = config.FavoriteGroups[draggedGroupIndex];
                        if (draggedRecipeIndex < sourceGroup.Recipes.Count)
                        {
                            var draggedFav = sourceGroup.Recipes[draggedRecipeIndex];
                            sourceGroup.Recipes.RemoveAt(draggedRecipeIndex);

                            var targetIndex = ri;
                            if (draggedGroupIndex == groupIndex && draggedRecipeIndex < ri)
                                targetIndex--;

                            group.Recipes.Insert(Math.Clamp(targetIndex, 0, group.Recipes.Count), draggedFav);
                            SaveConfig();
                        }

                        draggedGroupIndex = -1;
                        draggedRecipeIndex = -1;
                    }
                }
                ImGui.EndDragDropTarget();
            }

            // Right-align buttons
            ImGui.SameLine(ImGui.GetWindowWidth() - buttonsWidth);

            // Quick-add button
            if (ImGui.SmallButton($"Add x{fav.DefaultQuantity}"))
            {
                var mats = fav.Materials.Count > 0
                    ? fav.Materials.Select(m => new MaterialPreference
                        {
                            ItemId = m.ItemId,
                            ItemName = m.ItemName,
                            NqCount = m.NqCount,
                            HqCount = m.HqCount,
                        }).ToArray()
                    : ReadRecipeMaterials(fav.RecipeId);
                queueManager.AddItem(fav.RecipeId, fav.ItemName, fav.DefaultQuantity, mats);
                chatGui.Print($"[CraftQueue] Added {fav.DefaultQuantity}x {fav.ItemName} from favorites.");
            }

            ImGui.SameLine();

            // Edit default quantity
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

                ImGui.EndPopup();
            }

            // Remove button
            ImGui.SameLine();
            if (ImGui.SmallButton("X"))
            {
                toRemove = fav;
            }

            ImGui.PopID();
        }

        if (toRemove != null)
        {
            group.Recipes.Remove(toRemove);
            SaveConfig();
        }
    }

    // ─── Material reading (ported from MainWindow) ──────────────────────

    private MaterialPreference[] ReadRecipeMaterials(ushort recipeId)
    {
        try
        {
            var recipeSheet = dataManager.GetExcelSheet<Recipe>();
            var recipe = recipeSheet.GetRow(recipeId);
            var materials = new List<MaterialPreference>();

            foreach (var ing in recipe.Ingredient)
            {
                var item = ing.ValueNullable;
                if (item == null || item.Value.RowId == 0)
                    continue;

                materials.Add(new MaterialPreference
                {
                    ItemId = item.Value.RowId,
                    ItemName = item.Value.Name.ToString(),
                    NqCount = 0,
                    HqCount = 0,
                });
            }

            for (var i = 0; i < materials.Count && i < recipe.AmountIngredient.Count; i++)
            {
                materials[i].NqCount = recipe.AmountIngredient[i];
            }

            materials.RemoveAll(m => m.NqCount <= 0);
            return materials.ToArray();
        }
        catch (Exception ex)
        {
            log.Debug($"FavoritesWindow: Failed to read materials for recipe {recipeId}. {ex.Message}");
            return Array.Empty<MaterialPreference>();
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private void SaveConfig()
    {
        pluginInterface.SavePluginConfig(config);
    }

    public void Dispose()
    {
        // No resources to clean up
    }
}
