# CraftQueue — Claude Context

## What This Is
A Dalamud plugin for FFXIV that queues crafting recipes and drives Artisan to execute them automatically. Written in C# targeting .NET 10.0-windows, x64, Dalamud API Level 14.

## Build
```
cd src
dotnet build
```
Output: `src/bin/x64/Debug/CraftQueue.dll` — Dalamud hot-reloads it in-game.

## Project Structure
```
src/
  Plugin.cs                  — entry point, DI wiring, commands (/cq), event hooks
  Configuration.cs           — persisted settings, FavoriteGroup data model, migration
  Models/
    QueueItem.cs             — crafting queue item (Id, ItemName, Quantity, Materials, Status)
  Services/
    QueueManager.cs          — queue state: Add/Remove/Move/SetQuantity, drives Artisan
    ArtisanIpcBridge.cs      — IPC bridge to Artisan plugin
    RecipeNoteMonitor.cs     — watches crafting log to auto-populate recipe data
  Windows/
    MainWindow.cs            — primary UI: queue list, material summary, Artisan status bar
    FavoritesWindow.cs       — pop-out favorites window with groups and drag-drop
    SettingsWindow.cs        — settings UI
```

## Commands
- `/cq` — open main window
- `/cq favorites` / `/cq fav` — toggle favorites window
- `/cq settings` — open settings
- `/cq stop` — stop queue

## Key Patterns

### Window pattern
Each window is a class with `IsVisible`, `Draw()`, and `Dispose()`. Plugin.cs calls `Draw()` on each in `OnDraw()`. Constructor takes DI dependencies directly (no service locator).

### Configuration / persistence
`pluginInterface.SavePluginConfig(config)` / `GetPluginConfig()`. Call `SaveConfig()` (wrapper) after any mutation. Config version is checked in `MigrateIfNeeded()` on load.

### FavoriteGroup data model
```csharp
[Serializable]
public sealed class FavoriteGroup {
    public string Name { get; set; } = "General";
    public bool IsCollapsed { get; set; } = false;
    public List<FavoriteRecipe> Recipes { get; set; } = new();
}
// On Configuration:
public List<FavoriteGroup> FavoriteGroups { get; set; } = new();
public bool IsFavorite(ushort recipeId) { ... } // searches all groups
```
Config was v1 (flat `Favorites` list) → v2 (grouped). `MigrateIfNeeded()` handles upgrade.

### Drag-drop payload types
| Payload | Widget it's sourced from | Widget it targets | Purpose |
|---------|--------------------------|-------------------|---------|
| `"CQ_FAV"` | `SmallButton("=")` on recipe row | `Selectable` name on recipe row OR group header Selectable | Reorder favorites / move between groups |
| `"CQ_GROUP"` | `ArrowButton` on group header | `ArrowButton` on group header | Reorder groups |
| `"CQ_ITEM"` | `SmallButton("=")` on queue row | `Selectable` name on queue row | Reorder queue items |

---

## ImGui Rules (Hard-Won — Read Before Writing Any UI)

These were learned through extensive trial and error. Violating them causes subtle, hard-to-debug bugs.

### Drag-and-drop

**1. Non-interactive widgets cannot be drag sources.**
`Text`, `TextColored`, `Dummy` don't capture mouse input — the window steals the drag. Always use `SmallButton` or `InvisibleButton` as the drag handle.

**2. Always send a real payload byte.**
`SetDragDropPayload("TYPE", ReadOnlySpan<byte>.Empty)` results in `AcceptDragDropPayload` returning a struct where `Data == null`, so the drop is never processed. Use:
```csharp
private static readonly byte[] DragPayloadData = { 1 };
// ...
ImGui.SetDragDropPayload("CQ_FAV", DragPayloadData);
```

**3. Never put the drop target on the same widget as the drag source.**
When a widget is the active drag source or a highlighted drop target, ImGui suppresses it *and everything `SameLine`-chained after it*. The whole row disappears. Fix: use a **separate widget** on the same row as the drop target (e.g. a `Selectable` for the item name, while `SmallButton("=")` is the source).

**4. One `AcceptDragDropPayload` call per `BeginDragDropTarget` block.**
If you call it twice with different type strings, only the first call works — the second always gets null, even if the active payload matches. Give each payload type its own widget with its own `BeginDragDropTarget` block.

**5. One widget can be both source and target for the same payload type.**
`BeginDragDropSource` + `BeginDragDropTarget` on the same widget is fine. ImGui automatically prevents dropping onto the item currently being dragged.

**6. `BeginDragDropTargetCustom` and `NativePtr` are not available** in Dalamud's `Dalamud.Bindings.ImGui`. Don't attempt to use them.

**7. `stackalloc` in draw loops triggers CA2014.** Use `static readonly byte[]` for fixed payload data instead.

### Layout

**8. `CollapsingHeader` consumes the full row width** — `SameLine` buttons after it are visible but unclickable. `AllowOverlap` flag (bit 14) compiles but doesn't work in Dalamud's ImGui version. Replace with manual layout: `ArrowButton` (collapse toggle) + `Selectable` (label/drop target) + separate `SmallButton`s.

**9. `CollapsingHeader` cannot be a drop target.** It consumes the interaction. Use a `Selectable` instead.

**10. `Selectable` is the right widget for combined label + drop target.**
It's interactive (so `BeginDragDropTarget` works), highlights cleanly, can be sized precisely with `new Vector2(width, 0)`, and doesn't interfere with adjacent widgets. Standard pattern:
```csharp
var labelWidth = ImGui.GetWindowWidth() - ImGui.GetCursorPosX() - rightButtonsWidth - ImGui.GetStyle().WindowPadding.X;
ImGui.Selectable($"{label}###{uniqueId}", false, ImGuiSelectableFlags.None, new Vector2(labelWidth, 0));
if (ImGui.IsItemClicked()) { /* handle click */ }
if (ImGui.BeginDragDropTarget()) { /* handle drop */ }
// Then SameLine for right-aligned buttons — they're independent of the Selectable
ImGui.SameLine(ImGui.GetWindowWidth() - rightButtonsWidth);
```

**11. Overlay widgets (drawn before the real widget + cursor rewind) steal clicks unconditionally**, even when invisible. Only use this pattern conditionally (e.g., only during an active drag). Better: avoid it entirely and use the Selectable pattern above.

**12. Adding/removing widgets between rows causes jarring layout shifts.** Don't insert `InvisibleButton` drop zones between rows conditionally — the vertical expansion when drag starts is very noticeable. Use per-row drop targets on existing row content instead.

### ImGui.Text does not process ID separators

**13. `###` in `ImGui.Text()` / `TextColored()` is rendered literally.** The `###` ID separator (which hides the ID portion from display) only works in widgets that have an ImGui ID — `Button`, `Selectable`, `CollapsingHeader`, etc. Passing `"My Label###my_id"` to `ImGui.Text()` prints `My Label###my_id` verbatim. Use plain string interpolation for text, reserve `###` for interactive widgets.

---

## IGameInventory — Player Inventory API

Inject: `[PluginService] internal static IGameInventory GameInventory { get; private set; } = null!;`

```csharp
using Dalamud.Game.Inventory;   // GameInventoryType enum lives here

var slots = GameInventory.GetInventoryItems(GameInventoryType.Inventory1);
foreach (var slot in slots)
{
    uint  itemId   = slot.ItemId;    // base item ID (same for NQ and HQ)
    int   quantity = slot.Quantity;
    bool  isHq     = slot.IsHq;      // true = HQ copy of the item
}
```

- **Enum:** `GameInventoryType.Inventory1` through `Inventory4` for the 4 main bag pages.
- **HQ vs NQ:** HQ items have the same `ItemId` as their NQ counterpart — distinguish via `slot.IsHq`.
- **`InventoryType` from `FFXIVClientStructs.FFXIV.Client.Game` is a different type** — cannot be passed to `IGameInventory.GetInventoryItems()`. Always use `Dalamud.Game.Inventory.GameInventoryType`.

### NQ/HQ sufficiency logic
```csharp
// HQ stock satisfies HQ need first; leftover HQ covers NQ need too.
// NQ stock only satisfies NQ need.
var hqSatisfied  = haveHq >= hqNeed;
var leftoverHq   = Math.Max(0, haveHq - hqNeed);
var nqSatisfied  = (haveNq + leftoverHq) >= nqNeed;
var enough       = hqSatisfied && nqSatisfied;
```

---

## Auto-open Behaviour — FavoritesWindow

The FavoritesWindow is **user-controlled only**. It must NOT be opened automatically when:
- The crafting log opens (`OnCraftingLogOpened`)
- The main UI is requested (`OnOpenMainUi`)

`config.ShowFavorites` controls whether the *section* is shown in UI, not whether the pop-out window auto-opens. The pop-out is toggled only by the `F` button in the main window header or `/cq favorites`.
