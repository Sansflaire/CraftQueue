using System;

namespace CraftQueue.Models;

public enum QueueItemStatus
{
    Pending,
    Crafting,
    Completed,
    Failed,
}

public sealed class MaterialPreference
{
    public uint ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int NqCount { get; set; }
    public int HqCount { get; set; }
}

public sealed class QueueItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ushort RecipeId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
    public int CraftedSoFar { get; set; }
    public MaterialPreference[] Materials { get; set; } = Array.Empty<MaterialPreference>();
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public QueueItemStatus Status { get; set; } = QueueItemStatus.Pending;
}
