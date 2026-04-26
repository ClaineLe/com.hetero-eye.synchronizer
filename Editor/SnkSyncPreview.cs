using System.Collections.Generic;
using System.Linq;

public sealed class SnkSyncPreview
{
    public IReadOnlyList<SnkSyncItem> Creates { get; set; } = new List<SnkSyncItem>();
    public IReadOnlyList<SnkSyncItem> Updates { get; set; } = new List<SnkSyncItem>();
    public IReadOnlyList<SnkSyncItem> Deletes { get; set; } = new List<SnkSyncItem>();
    public IReadOnlyList<SnkSyncItem> Skips { get; set; } = new List<SnkSyncItem>();

    public int CreateCount => Creates.Count;
    public int UpdateCount => Updates.Count;
    public int DeleteCount => Deletes.Count;
    public int SkipCount => Skips.Count;

    public long WriteBytes => Creates.Sum(item => item.Size) + Updates.Sum(item => item.Size);
}
