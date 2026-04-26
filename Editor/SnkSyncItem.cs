public sealed class SnkSyncItem
{
    public string Path { get; set; } = string.Empty;
    public SnkSyncChangeType ChangeType { get; set; }
    public long Size { get; set; }
    public SnkRepositoryEntry SourceEntry { get; set; }
    public SnkRepositoryEntry TargetEntry { get; set; }
}
