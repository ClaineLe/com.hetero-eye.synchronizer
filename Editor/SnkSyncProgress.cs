public sealed class SnkSyncProgress
{
    public string Path { get; set; } = string.Empty;
    public SnkSyncChangeType ChangeType { get; set; }
    public int CompletedItems { get; set; }
    public int TotalItems { get; set; }
    public long TransferredBytes { get; set; }
    public long TotalBytes { get; set; }
    public float ItemProgress { get; set; }
    public bool ItemCompleted { get; set; }
    public bool ItemFailed { get; set; }
}
