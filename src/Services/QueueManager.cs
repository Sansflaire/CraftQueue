using System;
using System.Collections.Generic;
using System.Linq;

using CraftQueue.Models;

namespace CraftQueue.Services;

public sealed class QueueManager
{
    private readonly List<QueueItem> items = new();

    public IReadOnlyList<QueueItem> Items => items;
    public int Count => items.Count;

    public event Action? QueueChanged;

    public QueueItem AddItem(ushort recipeId, string itemName, int quantity, MaterialPreference[]? materials = null)
    {
        var item = new QueueItem
        {
            RecipeId = recipeId,
            ItemName = itemName,
            Quantity = Math.Clamp(quantity, 1, 9999),
            Materials = materials ?? Array.Empty<MaterialPreference>(),
        };

        items.Add(item);
        QueueChanged?.Invoke();
        return item;
    }

    public bool RemoveItem(Guid id)
    {
        var index = items.FindIndex(i => i.Id == id);
        if (index < 0)
            return false;

        items.RemoveAt(index);
        QueueChanged?.Invoke();
        return true;
    }

    public bool MoveItem(Guid id, int newIndex)
    {
        var oldIndex = items.FindIndex(i => i.Id == id);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= items.Count)
            return false;

        var item = items[oldIndex];
        items.RemoveAt(oldIndex);
        items.Insert(newIndex, item);
        QueueChanged?.Invoke();
        return true;
    }

    public bool SetQuantity(Guid id, int quantity)
    {
        var item = items.FirstOrDefault(i => i.Id == id);
        if (item == null)
            return false;

        item.Quantity = Math.Clamp(quantity, 1, 9999);
        QueueChanged?.Invoke();
        return true;
    }

    public bool SetStatus(Guid id, QueueItemStatus status)
    {
        var item = items.FirstOrDefault(i => i.Id == id);
        if (item == null)
            return false;

        item.Status = status;
        QueueChanged?.Invoke();
        return true;
    }

    public void ClearQueue()
    {
        items.Clear();
        QueueChanged?.Invoke();
    }

    public void ClearCompleted()
    {
        items.RemoveAll(i => i.Status == QueueItemStatus.Completed);
        QueueChanged?.Invoke();
    }

    public QueueItem? GetFirstPending()
    {
        return items.FirstOrDefault(i => i.Status == QueueItemStatus.Pending);
    }

    public QueueItem? GetById(Guid id)
    {
        return items.FirstOrDefault(i => i.Id == id);
    }
}
