using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed class SnkSynchronizer
{
    private const int DefaultMaxConcurrentWrites = 4;

    public async Task<SnkSyncPreview> PreviewAsync(
        SnkRepositoryReader source,
        SnkRepositoryReader target,
        CancellationToken cancellationToken = default)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (target == null) throw new ArgumentNullException(nameof(target));

        var sourceEntries = await source.ListAsync(cancellationToken).ConfigureAwait(false);
        var targetEntries = await target.ListAsync(cancellationToken).ConfigureAwait(false);

        var sourceMap = ToMap(sourceEntries, source.Name, true);
        var targetMap = ToMap(targetEntries, target.Name, false);

        var creates = new List<SnkSyncItem>();
        var updates = new List<SnkSyncItem>();
        var deletes = new List<SnkSyncItem>();
        var skips = new List<SnkSyncItem>();

        foreach (var pair in sourceMap)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!targetMap.TryGetValue(pair.Key, out var targetEntry))
            {
                creates.Add(CreateItem(pair.Key, SnkSyncChangeType.Create, pair.Value, null));
                continue;
            }

            if (IsSame(pair.Value, targetEntry))
            {
                skips.Add(CreateItem(pair.Key, SnkSyncChangeType.Skip, pair.Value, targetEntry));
                continue;
            }

            updates.Add(CreateItem(pair.Key, SnkSyncChangeType.Update, pair.Value, targetEntry));
        }

        foreach (var pair in targetMap)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sourceMap.ContainsKey(pair.Key))
            {
                continue;
            }

            deletes.Add(CreateItem(pair.Key, SnkSyncChangeType.Delete, null, pair.Value));
        }

        return new SnkSyncPreview
        {
            Creates = creates,
            Updates = updates,
            Deletes = deletes,
            Skips = skips
        };
    }

    public async Task ApplyAsync(
        SnkRepositoryReader source,
        SnkRepositoryWriter target,
        SnkSyncPreview preview,
        IProgress<SnkSyncProgress> progress = null,
        CancellationToken cancellationToken = default,
        int maxConcurrentWrites = DefaultMaxConcurrentWrites)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (preview == null) throw new ArgumentNullException(nameof(preview));

        var pendingWrites = preview.Creates.Concat(preview.Updates).ToList();
        var pendingDeletes = preview.Deletes.ToList();
        var totalItems = pendingWrites.Count + pendingDeletes.Count;
        var totalBytes = preview.WriteBytes;
        var completedItems = 0;
        long transferredBytes = 0;
        var stateLock = new object();

        var boundedConcurrentWrites = Math.Max(1, Math.Min(maxConcurrentWrites, 5));
        using (var gate = new SemaphoreSlim(boundedConcurrentWrites))
        {
            var writeTasks = pendingWrites.Select(item => ApplyWriteAsync(
                source,
                target,
                item,
                progress,
                gate,
                stateLock,
                () => completedItems,
                value => completedItems = value,
                () => transferredBytes,
                value => transferredBytes = value,
                totalItems,
                totalBytes,
                cancellationToken));
            await Task.WhenAll(writeTasks).ConfigureAwait(false);
        }

        foreach (var item in pendingDeletes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReportProgress(progress, item, completedItems, totalItems, transferredBytes, totalBytes, 0.5f, false);
            await target.DeleteAsync(item.Path, cancellationToken).ConfigureAwait(false);

            lock (stateLock)
            {
                completedItems++;
            }

            ReportProgress(progress, item, completedItems, totalItems, transferredBytes, totalBytes, 1f, true);
        }
    }

    private static async Task ApplyWriteAsync(
        SnkRepositoryReader source,
        SnkRepositoryWriter target,
        SnkSyncItem item,
        IProgress<SnkSyncProgress> progress,
        SemaphoreSlim gate,
        object stateLock,
        Func<int> getCompletedItems,
        Action<int> setCompletedItems,
        Func<long> getTransferredBytes,
        Action<long> setTransferredBytes,
        int totalItems,
        long totalBytes,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReportProgress(
                progress,
                item,
                getCompletedItems(),
                totalItems,
                getTransferredBytes(),
                totalBytes,
                0.01f,
                false);

            using var stream = await source.OpenReadAsync(item.Path, cancellationToken).ConfigureAwait(false);
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            using var progressStream = new ProgressReadStream(stream, item.Size, itemProgress =>
            {
                ReportProgress(
                    progress,
                    item,
                    getCompletedItems(),
                    totalItems,
                    getTransferredBytes(),
                    totalBytes,
                    itemProgress,
                    false);
            });

            await target.WriteAsync(item.Path, progressStream, cancellationToken).ConfigureAwait(false);

            int completedItems;
            long transferredBytes;
            lock (stateLock)
            {
                transferredBytes = getTransferredBytes() + item.Size;
                setTransferredBytes(transferredBytes);
                completedItems = getCompletedItems() + 1;
                setCompletedItems(completedItems);
            }

            ReportProgress(progress, item, completedItems, totalItems, transferredBytes, totalBytes, 1f, true);
        }
        catch
        {
            ReportProgress(
                progress,
                item,
                getCompletedItems(),
                totalItems,
                getTransferredBytes(),
                totalBytes,
                0f,
                false,
                true);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    private static void ReportProgress(
        IProgress<SnkSyncProgress> progress,
        SnkSyncItem item,
        int completedItems,
        int totalItems,
        long transferredBytes,
        long totalBytes,
        float itemProgress,
        bool itemCompleted,
        bool itemFailed = false)
    {
        progress?.Report(new SnkSyncProgress
        {
            Path = item.Path,
            ChangeType = item.ChangeType,
            CompletedItems = completedItems,
            TotalItems = totalItems,
            TransferredBytes = transferredBytes,
            TotalBytes = totalBytes,
            ItemProgress = itemProgress,
            ItemCompleted = itemCompleted,
            ItemFailed = itemFailed
        });
    }

    private static Dictionary<string, SnkRepositoryEntry> ToMap(
        IReadOnlyList<SnkRepositoryEntry> entries,
        string repositoryName,
        bool requireContentMd5)
    {
        var map = new Dictionary<string, SnkRepositoryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var path = SnkPathUtility.NormalizeRelativePath(entry.Path);
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            entry.Path = path;
            entry.ContentMd5 = SnkMd5Utility.NormalizeMd5(entry.ContentMd5);
            if (requireContentMd5 && string.IsNullOrEmpty(entry.ContentMd5))
            {
                throw new InvalidOperationException($"仓库缺少有效 MD5：{repositoryName}/{path}");
            }

            map[path] = entry;
        }

        return map;
    }

    private static bool IsSame(SnkRepositoryEntry source, SnkRepositoryEntry target)
    {
        if (source.Size != target.Size)
        {
            return false;
        }

        return string.Equals(source.ContentMd5, target.ContentMd5, StringComparison.OrdinalIgnoreCase);
    }

    private static SnkSyncItem CreateItem(
        string path,
        SnkSyncChangeType changeType,
        SnkRepositoryEntry sourceEntry,
        SnkRepositoryEntry targetEntry)
    {
        return new SnkSyncItem
        {
            Path = path,
            ChangeType = changeType,
            Size = sourceEntry?.Size ?? targetEntry?.Size ?? 0,
            SourceEntry = sourceEntry,
            TargetEntry = targetEntry
        };
    }

    private sealed class ProgressReadStream : Stream
    {
        private const long MinReportTickInterval = 1000000L;

        private readonly Stream _inner;
        private readonly long _totalBytes;
        private readonly Action<float> _onProgress;
        private long _readBytes;
        private long _lastReportTicks;
        private float _lastReportedProgress;

        public ProgressReadStream(Stream inner, long totalBytes, Action<float> onProgress)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _totalBytes = Math.Max(0, totalBytes);
            _onProgress = onProgress;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            ReportRead(read);
            return read;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            var read = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            ReportRead(read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var position = _inner.Seek(offset, origin);
            _readBytes = Math.Max(0, position);
            return position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        private void ReportRead(int read)
        {
            if (read <= 0 || _onProgress == null)
            {
                return;
            }

            _readBytes += read;
            var progress = _totalBytes > 0
                ? (float)Math.Min(0.99d, Math.Max(0.01d, (double)_readBytes / _totalBytes))
                : 0.99f;
            var nowTicks = DateTime.UtcNow.Ticks;

            if (progress < 0.99f &&
                progress - _lastReportedProgress < 0.01f &&
                nowTicks - _lastReportTicks < MinReportTickInterval)
            {
                return;
            }

            _lastReportedProgress = progress;
            _lastReportTicks = nowTicks;
            _onProgress(progress);
        }
    }
}
