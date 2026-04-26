using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aliyun.OSS;
using Aliyun.OSS.Common;

public sealed class SnkAliyunOssRepository : SnkRepository
{
    private readonly OssClient _client;
    private readonly OssClient _uploadClient;
    private readonly SnkAliyunOssRepositoryOptions _options;
    private readonly string _objectPrefix;

    public SnkAliyunOssRepository(SnkAliyunOssRepositoryOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();

        _objectPrefix = SnkPathUtility.NormalizePrefix(_options.ObjectPrefix);
        _client = CreateClient(_options, _options.ConnectionTimeoutMilliseconds);
        _uploadClient = CreateClient(_options, _options.UploadTimeoutMilliseconds);
        Name = string.IsNullOrWhiteSpace(_options.Name)
            ? $"AliyunOSS:{_options.BucketName}/{_objectPrefix}"
            : _options.Name;
    }

    public string Name { get; }

    public Task<IReadOnlyList<SnkRepositoryEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        return RunOssOperationAsync(
            "列举远端对象",
            _objectPrefix,
            () =>
            {
                var result = new List<SnkRepositoryEntry>();
                string marker = null;

                do
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var request = new ListObjectsRequest(_options.BucketName)
                    {
                        Prefix = _objectPrefix,
                        Marker = marker,
                        MaxKeys = 1000
                    };

                    var listing = _client.ListObjects(request);
                    foreach (var summary in listing.ObjectSummaries ?? Enumerable.Empty<OssObjectSummary>())
                    {
                        if (string.IsNullOrEmpty(summary.Key) || summary.Key.EndsWith("/", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var relativePath = SnkPathUtility.StripPrefix(summary.Key, _objectPrefix);
                        result.Add(new SnkRepositoryEntry
                        {
                            Path = relativePath,
                            Size = summary.Size,
                            ContentMd5 = SnkMd5Utility.NormalizeMd5(summary.ETag),
                            LastModifiedUtc = summary.LastModified
                        });
                    }

                    marker = listing.NextMarker;
                    if (!listing.IsTruncated)
                    {
                        break;
                    }
                } while (true);

                return (IReadOnlyList<SnkRepositoryEntry>)result;
            },
            cancellationToken);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        return RunOssOperationAsync(
            "读取远端对象",
            path,
            () =>
            {
                var request = new GetObjectRequest(_options.BucketName, ToObjectKey(path));
                var output = new MemoryStream();
                _client.GetObject(request, output);
                output.Position = 0;
                return (Stream)output;
            },
            cancellationToken);
    }

    public Task WriteAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));

        return RunOssOperationAsync(
            "上传文件",
            path,
            () =>
            {
                if (content.CanSeek)
                {
                    content.Position = 0;
                }

                var uploadContent = EnsureSeekable(content);
                var shouldDisposeUploadContent = !ReferenceEquals(uploadContent, content);

                try
                {
                    uploadContent.Position = 0;
                    var request = new PutObjectRequest(_options.BucketName, ToObjectKey(path), uploadContent);
                    _uploadClient.PutObject(request);
                }
                finally
                {
                    if (shouldDisposeUploadContent)
                    {
                        uploadContent.Dispose();
                    }
                }
            },
            cancellationToken);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        return RunOssOperationAsync(
            "删除远端对象",
            path,
            () => _client.DeleteObject(_options.BucketName, ToObjectKey(path)),
            cancellationToken);
    }

    private string ToObjectKey(string path)
    {
        return SnkPathUtility.CombineKey(_objectPrefix, path);
    }

    private static Stream EnsureSeekable(Stream content)
    {
        if (content.CanSeek)
        {
            return content;
        }

        var memoryStream = new MemoryStream();
        content.CopyTo(memoryStream);
        memoryStream.Position = 0;
        return memoryStream;
    }

    private Task RunOssOperationAsync(
        string operationName,
        string path,
        Action operation,
        CancellationToken cancellationToken)
    {
        return RunOssOperationAsync(
            operationName,
            path,
            () =>
            {
                operation();
                return true;
            },
            cancellationToken);
    }

    private Task<T> RunOssOperationAsync<T>(
        string operationName,
        string path,
        Func<T> operation,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return operation();
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw CreateOssException(operationName, path, exception);
            }
        }, cancellationToken);
    }

    private Exception CreateOssException(string operationName, string path, Exception exception)
    {
        var unwrapped = UnwrapException(exception);
        var objectKey = ToObjectKey(path);
        var message = $"OSS {operationName}失败：{_options.BucketName}/{objectKey}";

        if (unwrapped is TaskCanceledException)
        {
            var timeoutMilliseconds = string.Equals(operationName, "上传文件", StringComparison.Ordinal)
                ? _options.UploadTimeoutMilliseconds
                : _options.ConnectionTimeoutMilliseconds;
            return new TimeoutException(
                $"{message}。请求超时，当前操作超时为 {timeoutMilliseconds / 1000} 秒。",
                unwrapped);
        }

        if (unwrapped is OssException ossException)
        {
            return new InvalidOperationException(
                $"{message}。OSS错误码：{ossException.ErrorCode}，RequestId：{ossException.RequestId}，Message：{ossException.Message}",
                ossException);
        }

        return new InvalidOperationException(message, unwrapped);
    }

    private static Exception UnwrapException(Exception exception)
    {
        if (exception is AggregateException aggregateException && aggregateException.InnerExceptions.Count == 1)
        {
            return aggregateException.InnerExceptions[0];
        }

        return exception;
    }

    private static OssClient CreateClient(SnkAliyunOssRepositoryOptions options, int timeoutMilliseconds)
    {
        var configuration = new ClientConfiguration
        {
            ConnectionTimeout = timeoutMilliseconds,
            MaxErrorRetry = options.MaxErrorRetry
        };

        if (string.IsNullOrWhiteSpace(options.SecurityToken))
        {
            return new OssClient(options.Endpoint, options.AccessKeyId, options.AccessKeySecret, configuration);
        }

        return new OssClient(
            options.Endpoint,
            options.AccessKeyId,
            options.AccessKeySecret,
            options.SecurityToken,
            configuration);
    }
}
