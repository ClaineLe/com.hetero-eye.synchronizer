using System;

public sealed class SnkAliyunOssRepositoryOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string ObjectPrefix { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string AccessKeySecret { get; set; } = string.Empty;
    public string SecurityToken { get; set; } = string.Empty;
    public int ConnectionTimeoutMilliseconds { get; set; } = 10000;
    public int UploadTimeoutMilliseconds { get; set; } = 600000;
    public int MaxErrorRetry { get; set; } = 2;
    public string Name { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Endpoint))
            throw new ArgumentException("Endpoint is required.", nameof(Endpoint));

        if (string.IsNullOrWhiteSpace(BucketName))
            throw new ArgumentException("BucketName is required.", nameof(BucketName));

        if (string.IsNullOrWhiteSpace(AccessKeyId))
            throw new ArgumentException("AccessKeyId is required.", nameof(AccessKeyId));

        if (string.IsNullOrWhiteSpace(AccessKeySecret))
            throw new ArgumentException("AccessKeySecret is required.", nameof(AccessKeySecret));
    }
}
