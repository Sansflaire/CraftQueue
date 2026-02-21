using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;

namespace CraftQueue.Windows;

public sealed class SettingsWindow : IDisposable
{
    private readonly Configuration config;
    private readonly IDalamudPluginInterface pluginInterface;

    public bool IsVisible { get; set; }

    public SettingsWindow(Configuration config, IDalamudPluginInterface pluginInterface)
    {
        this.config = config;
        this.pluginInterface = pluginInterface;
    }

    public void Draw()
    {
        if (!IsVisible)
            return;

        ImGui.SetNextWindowSize(new Vector2(420, 400), ImGuiCond.FirstUseEver);

        var visible = IsVisible;
        if (ImGui.Begin("Craft Queue Settings", ref visible, ImGuiWindowFlags.NoCollapse))
        {
            DrawCraftingBehavior();
            ImGui.Spacing();
            DrawWindowBehavior();
            ImGui.Spacing();
            DrawQueueBehavior();
            ImGui.Spacing();
            DrawArtisanIntegration();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Save & Close"))
            {
                Save();
                visible = false;
            }

            ImGui.SameLine();

            if (ImGui.Button("Reset to Defaults"))
            {
                ResetDefaults();
            }
        }

        ImGui.End();
        IsVisible = visible;
    }

    private void DrawCraftingBehavior()
    {
        if (ImGui.CollapsingHeader("Crafting Behavior", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var autoCraft = config.AutoCraftEntireList;
            if (ImGui.Checkbox("Automatically Craft Entire List", ref autoCraft))
            {
                config.AutoCraftEntireList = autoCraft;
                Save();
            }

            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
                "  When checked, 'Craft All' processes items top-to-bottom.\n" +
                "  When unchecked, button becomes 'Craft Next' and crafts\n" +
                "  only the first pending item.");
        }
    }

    private void DrawWindowBehavior()
    {
        if (ImGui.CollapsingHeader("Window Behavior", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var autoOpen = config.AutoOpenWithCraftingLog;
            if (ImGui.Checkbox("Auto-open when Crafting Log opens", ref autoOpen))
            {
                config.AutoOpenWithCraftingLog = autoOpen;
                Save();
            }

            var autoClose = config.AutoCloseWithCraftingLog;
            if (ImGui.Checkbox("Auto-close when Crafting Log closes", ref autoClose))
            {
                config.AutoCloseWithCraftingLog = autoClose;
                Save();
            }

            var showFavorites = config.ShowFavorites;
            if (ImGui.Checkbox("Auto-open Favorites window with main window", ref showFavorites))
            {
                config.ShowFavorites = showFavorites;
                Save();
            }

            var compact = config.CompactMode;
            if (ImGui.Checkbox("Compact mode (smaller item rows)", ref compact))
            {
                config.CompactMode = compact;
                Save();
            }
        }
    }

    private void DrawQueueBehavior()
    {
        if (ImGui.CollapsingHeader("Queue Behavior", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var autoRemove = config.AutoRemoveCompleted;
            if (ImGui.Checkbox("Remove completed items automatically", ref autoRemove))
            {
                config.AutoRemoveCompleted = autoRemove;
                Save();
            }

            var confirm = config.ConfirmBeforeRemoving;
            if (ImGui.Checkbox("Confirm before removing items", ref confirm))
            {
                config.ConfirmBeforeRemoving = confirm;
                Save();
            }
        }
    }

    private void DrawArtisanIntegration()
    {
        if (ImGui.CollapsingHeader("Artisan Integration", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var showStatus = config.ShowArtisanStatus;
            if (ImGui.Checkbox("Show Artisan status in window", ref showStatus))
            {
                config.ShowArtisanStatus = showStatus;
                Save();
            }

            ImGui.Text("Polling interval (ms):");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);

            var polling = config.PollingIntervalMs;
            if (ImGui.InputInt("##polling", ref polling))
            {
                config.PollingIntervalMs = Math.Clamp(polling, 100, 5000);
                Save();
            }

            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
                "  How often to check Artisan's status (100-5000ms).");
        }
    }

    private void Save()
    {
        pluginInterface.SavePluginConfig(config);
    }

    private void ResetDefaults()
    {
        config.AutoCraftEntireList = true;
        config.AutoOpenWithCraftingLog = true;
        config.AutoCloseWithCraftingLog = false;
        config.ShowFavorites = true;
        config.CompactMode = false;
        config.AutoRemoveCompleted = true;
        config.ConfirmBeforeRemoving = false;
        config.ShowArtisanStatus = true;
        config.PollingIntervalMs = 500;
        Save();
    }

    public void Dispose()
    {
        // No resources to clean up
    }
}
