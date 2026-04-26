using System;

public sealed class SnkRepositoryEntry
{
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public string ContentMd5 { get; set; } = string.Empty;
    public DateTimeOffset? LastModifiedUtc { get; set; }
}
