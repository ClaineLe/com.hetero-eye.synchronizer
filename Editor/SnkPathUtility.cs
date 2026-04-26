using System;

public static class SnkPathUtility
{
    public static string NormalizeRelativePath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').TrimStart('/');
    }

    public static string CombineKey(string prefix, string relativePath)
    {
        var normalizedPrefix = NormalizePrefix(prefix);
        var normalizedPath = NormalizeRelativePath(relativePath);
        return string.IsNullOrEmpty(normalizedPrefix)
            ? normalizedPath
            : normalizedPrefix + normalizedPath;
    }

    public static string NormalizePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return string.Empty;
        }

        return prefix.Trim().Trim('/').Replace('\\', '/') + "/";
    }

    public static string StripPrefix(string key, string prefix)
    {
        var normalizedKey = NormalizeRelativePath(key);
        var normalizedPrefix = NormalizePrefix(prefix);
        if (string.IsNullOrEmpty(normalizedPrefix))
        {
            return normalizedKey;
        }

        return normalizedKey.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)
            ? normalizedKey.Substring(normalizedPrefix.Length)
            : normalizedKey;
    }
}
