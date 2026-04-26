using System.IO;
using System.Threading;
using System.Threading.Tasks;

public interface SnkRepositoryWriter
{
    string Name { get; }

    Task WriteAsync(string path, Stream content, CancellationToken cancellationToken = default);

    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}
