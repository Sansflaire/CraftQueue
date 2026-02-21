using System;

using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace CraftQueue.Services;

public sealed class ArtisanIpcBridge : IDisposable
{
    private readonly IPluginLog log;

    // IPC subscribers
    private ICallGateSubscriber<ushort, int, object>? craftItem;
    private ICallGateSubscriber<bool>? isBusy;
    private ICallGateSubscriber<bool>? isListRunning;
    private ICallGateSubscriber<bool>? isListPaused;
    private ICallGateSubscriber<bool>? getEnduranceStatus;
    private ICallGateSubscriber<bool>? getStopRequest;
    private ICallGateSubscriber<bool, object>? setEnduranceStatus;
    private ICallGateSubscriber<bool, object>? setListPause;
    private ICallGateSubscriber<bool, object>? setStopRequest;

    public bool ArtisanAvailable { get; private set; }

    private bool subscribersCreated;

    public ArtisanIpcBridge(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;

        try
        {
            craftItem = pluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
            isBusy = pluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy");
            isListRunning = pluginInterface.GetIpcSubscriber<bool>("Artisan.IsListRunning");
            isListPaused = pluginInterface.GetIpcSubscriber<bool>("Artisan.IsListPaused");
            getEnduranceStatus = pluginInterface.GetIpcSubscriber<bool>("Artisan.GetEnduranceStatus");
            getStopRequest = pluginInterface.GetIpcSubscriber<bool>("Artisan.GetStopRequest");
            setEnduranceStatus = pluginInterface.GetIpcSubscriber<bool, object>("Artisan.SetEnduranceStatus");
            setListPause = pluginInterface.GetIpcSubscriber<bool, object>("Artisan.SetListPause");
            setStopRequest = pluginInterface.GetIpcSubscriber<bool, object>("Artisan.SetStopRequest");
            subscribersCreated = true;
            log.Info("ArtisanIpcBridge: All IPC subscribers registered.");
        }
        catch (Exception ex)
        {
            subscribersCreated = false;
            log.Warning($"ArtisanIpcBridge: Artisan IPC not available. {ex.Message}");
        }

        // Probe once at startup
        CheckAvailability();
    }

    /// <summary>
    /// Probes Artisan by actually calling IsBusy via IPC.
    /// GetIpcSubscriber always succeeds even if the target plugin is unloaded,
    /// so we must attempt a real IPC call to know if Artisan is responding.
    /// Call this periodically from the polling loop.
    /// </summary>
    public void CheckAvailability()
    {
        if (!subscribersCreated || isBusy == null)
        {
            ArtisanAvailable = false;
            return;
        }

        try
        {
            isBusy.InvokeFunc();
            if (!ArtisanAvailable)
            {
                ArtisanAvailable = true;
                log.Info("ArtisanIpcBridge: Artisan is now available.");
            }
        }
        catch
        {
            if (ArtisanAvailable)
            {
                ArtisanAvailable = false;
                log.Info("ArtisanIpcBridge: Artisan is no longer available.");
            }
        }
    }

    public void CraftItem(ushort recipeId, int quantity)
    {
        if (!ArtisanAvailable || craftItem == null)
        {
            log.Error("ArtisanIpcBridge: Cannot craft — Artisan not available.");
            return;
        }

        try
        {
            craftItem.InvokeAction(recipeId, quantity);
            log.Info($"ArtisanIpcBridge: Sent CraftItem(recipeId={recipeId}, qty={quantity})");
        }
        catch (Exception ex)
        {
            log.Error($"ArtisanIpcBridge: CraftItem failed. {ex.Message}");
        }
    }

    public bool IsBusy()
    {
        return SafeInvoke(isBusy, false);
    }

    public bool IsListRunning()
    {
        return SafeInvoke(isListRunning, false);
    }

    public bool IsListPaused()
    {
        return SafeInvoke(isListPaused, false);
    }

    public bool GetEnduranceStatus()
    {
        return SafeInvoke(getEnduranceStatus, false);
    }

    public bool GetStopRequest()
    {
        return SafeInvoke(getStopRequest, false);
    }

    public void SetEnduranceStatus(bool enabled)
    {
        SafeInvokeAction(setEnduranceStatus, enabled);
    }

    public void SetListPause(bool paused)
    {
        SafeInvokeAction(setListPause, paused);
    }

    public void SetStopRequest(bool stop)
    {
        SafeInvokeAction(setStopRequest, stop);
    }

    private bool SafeInvoke(ICallGateSubscriber<bool>? subscriber, bool fallback)
    {
        if (!ArtisanAvailable || subscriber == null)
            return fallback;

        try
        {
            return subscriber.InvokeFunc();
        }
        catch (Exception ex)
        {
            log.Debug($"ArtisanIpcBridge: IPC call failed. {ex.Message}");
            return fallback;
        }
    }

    private void SafeInvokeAction(ICallGateSubscriber<bool, object>? subscriber, bool value)
    {
        if (!ArtisanAvailable || subscriber == null)
            return;

        try
        {
            subscriber.InvokeAction(value);
        }
        catch (Exception ex)
        {
            log.Error($"ArtisanIpcBridge: IPC action failed. {ex.Message}");
        }
    }

    public void Dispose()
    {
        // IPC subscribers don't need explicit cleanup — they're garbage collected.
    }
}
