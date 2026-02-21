using System;

using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

using CraftQueue.Services;
using CraftQueue.Windows;

namespace CraftQueue;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    private const string CommandMain = "/craftqueue";
    private const string CommandShort = "/cq";

    private readonly Configuration config;
    private readonly ArtisanIpcBridge artisan;
    private readonly RecipeNoteMonitor recipeMonitor;
    private readonly QueueManager queueManager;
    private readonly MainWindow mainWindow;
    private readonly SettingsWindow settingsWindow;

    private DateTime lastPoll = DateTime.MinValue;

    public Plugin()
    {
        // Load config
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Create services
        artisan = new ArtisanIpcBridge(PluginInterface, Log);
        recipeMonitor = new RecipeNoteMonitor(AddonLifecycle, Log);
        queueManager = new QueueManager();
        settingsWindow = new SettingsWindow(config, PluginInterface);
        mainWindow = new MainWindow(queueManager, artisan, recipeMonitor, config, PluginInterface, settingsWindow, DataManager, Condition, ChatGui, Log);

        // Wire up events
        recipeMonitor.CraftingLogOpened += OnCraftingLogOpened;
        recipeMonitor.CraftingLogClosed += OnCraftingLogClosed;

        // Register commands
        var commandInfo = new CommandInfo(OnCommand)
        {
            HelpMessage =
                "Craft Queue commands:\n" +
                "  /cq              Toggle the queue window\n" +
                "  /cq settings     Open settings\n" +
                "  /cq clear        Clear the queue\n" +
                "  /cq craft        Start crafting\n" +
                "  /cq stop         Stop crafting\n" +
                "  /cq pause        Pause crafting\n" +
                "  /cq resume       Resume crafting",
        };

        CommandManager.AddHandler(CommandMain, commandInfo);
        CommandManager.AddHandler(CommandShort, commandInfo);

        // Register UI callbacks
        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenMainUi += OnOpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;

        // Register framework update for polling
        Framework.Update += OnFrameworkUpdate;

        Log.Info("CraftQueue loaded. Use /cq to open.");
    }

    public void Dispose()
    {
        // Unregister framework
        Framework.Update -= OnFrameworkUpdate;

        // Unregister UI
        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenMainUi -= OnOpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;

        // Unregister commands
        CommandManager.RemoveHandler(CommandMain);
        CommandManager.RemoveHandler(CommandShort);

        // Unwire events
        recipeMonitor.CraftingLogOpened -= OnCraftingLogOpened;
        recipeMonitor.CraftingLogClosed -= OnCraftingLogClosed;

        // Dispose services
        settingsWindow.Dispose();
        mainWindow.Dispose();
        recipeMonitor.Dispose();
        artisan.Dispose();

        // Save config
        PluginInterface.SavePluginConfig(config);

        Log.Info("CraftQueue unloaded.");
    }

    private void OnCommand(string command, string args)
    {
        var sub = args.Trim().ToLower();

        switch (sub)
        {
            case "":
                mainWindow.IsVisible = !mainWindow.IsVisible;
                break;

            case "settings":
                settingsWindow.IsVisible = !settingsWindow.IsVisible;
                break;

            case "clear":
                queueManager.ClearQueue();
                ChatGui.Print("[CraftQueue] Queue cleared.");
                break;

            case "craft":
                mainWindow.IsVisible = true;
                // Trigger craft-all via the window's logic
                ChatGui.Print("[CraftQueue] Use the Craft All button in the window.");
                break;

            case "stop":
                artisan.SetStopRequest(true);
                ChatGui.Print("[CraftQueue] Stop requested.");
                break;

            case "pause":
                artisan.SetListPause(true);
                ChatGui.Print("[CraftQueue] Paused.");
                break;

            case "resume":
                artisan.SetListPause(false);
                ChatGui.Print("[CraftQueue] Resumed.");
                break;

            default:
                ChatGui.Print($"[CraftQueue] Unknown command: {sub}. Use /cq for help.");
                break;
        }
    }

    private void OnCraftingLogOpened()
    {
        if (config.AutoOpenWithCraftingLog)
            mainWindow.IsVisible = true;
    }

    private void OnCraftingLogClosed()
    {
        if (config.AutoCloseWithCraftingLog)
            mainWindow.IsVisible = false;
    }

    private void OnOpenMainUi()
    {
        mainWindow.IsVisible = true;
    }

    private void OnOpenConfigUi()
    {
        settingsWindow.IsVisible = true;
    }

    private void OnDraw()
    {
        mainWindow.Draw();
        settingsWindow.Draw();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // Throttle polling
        var now = DateTime.Now;
        if ((now - lastPoll).TotalMilliseconds < config.PollingIntervalMs)
            return;

        lastPoll = now;

        mainWindow.UpdateArtisanStatus();
        mainWindow.UpdateSelectedRecipe();
        mainWindow.UpdateCraftingProgress();
    }
}
