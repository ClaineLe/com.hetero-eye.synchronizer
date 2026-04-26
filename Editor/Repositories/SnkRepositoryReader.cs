using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public interface SnkRepositoryReader
{
    string Name { get; }

    Task<IReadOnlyList<SnkRepositoryEntry>> ListAsync(CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);
}
