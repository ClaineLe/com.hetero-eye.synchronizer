using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public sealed class SnkLocalRepository : SnkRepository
{
    private readonly string _rootPath;

    public SnkLocalRepository(string rootPath, string name = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));

        _rootPath = Path.GetFullPath(rootPath);
        Name = string.IsNullOrWhiteSpace(name) ? $"Local:{_rootPath}" : name;
    }

    public string Name { get; }

    public Task<IReadOnlyList<SnkRepositoryEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SnkRepositoryEntry> result = ListInternal(cancellationToken);
        return Task.FromResult(result);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = GetFullPath(path);
        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }

    public async Task WriteAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (content == null) throw new ArgumentNullException(nameof(content));

        var fullPath = GetFullPath(path);
        var directoryPath = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        using var output = File.Create(fullPath);
        await content.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = GetFullPath(path);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private IReadOnlyList<SnkRepositoryEntry> ListInternal(CancellationToken cancellationToken)
    {
        var result = new List<SnkRepositoryEntry>();
        if (!Directory.Exists(_rootPath))
        {
            return result;
        }

        foreach (var filePath in Directory.EnumerateFiles(_rootPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(filePath);
            var relativePath = SnkPathUtility.NormalizeRelativePath(Path.GetRelativePath(_rootPath, filePath));
            result.Add(new SnkRepositoryEntry
            {
                Path = relativePath,
                Size = fileInfo.Length,
                ContentMd5 = SnkMd5Utility.ComputeFileMd5(filePath),
                LastModifiedUtc = fileInfo.LastWriteTimeUtc
            });
        }

        return result;
    }

    private string GetFullPath(string path)
    {
        var relativePath = SnkPathUtility.NormalizeRelativePath(path);
        return Path.Combine(_rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

}
