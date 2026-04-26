using System;
using System.IO;
using System.Security.Cryptography;

public static class SnkMd5Utility
{
    public static string ComputeFileMd5(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return ComputeStreamMd5(stream);
    }

    public static string ComputeStreamMd5(Stream stream)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead) throw new InvalidOperationException("Stream must be readable.");

        var originalPosition = stream.CanSeek ? stream.Position : 0;
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(stream);

        if (stream.CanSeek)
        {
            stream.Position = originalPosition;
        }

        return ToHex(hash);
    }

    public static string NormalizeMd5(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Trim('"').ToLowerInvariant();

        return IsMd5(normalized) ? normalized : string.Empty;
    }

    public static bool IsMd5(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 32)
        {
            return false;
        }

        foreach (var ch in value)
        {
            var isHex =
                (ch >= '0' && ch <= '9') ||
                (ch >= 'a' && ch <= 'f') ||
                (ch >= 'A' && ch <= 'F');

            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    private static string ToHex(byte[] bytes)
    {
        var chars = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var value = bytes[i];
            chars[i * 2] = GetHexChar(value >> 4);
            chars[i * 2 + 1] = GetHexChar(value & 0xF);
        }

        return new string(chars);
    }

    private static char GetHexChar(int value)
    {
        return (char)(value < 10 ? '0' + value : 'a' + value - 10);
    }
}
