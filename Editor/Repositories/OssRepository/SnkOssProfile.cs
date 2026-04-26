using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[CreateAssetMenu(fileName = "OssProfile", menuName = "Snk/OSS Profile")]
public sealed class SnkOssProfile : ScriptableObject
{
    public const string DefaultAssetPath = "Assets/Editor Resources/OssProfile.asset";

    [Header("阿里云 OSS")]
    public string Endpoint = string.Empty;
    public string BucketName = string.Empty;

    [Header("账号")]
    public string AccessKeyId = string.Empty;
    public string AccessKeySecret = string.Empty;
    public string SecurityToken = string.Empty;

    public SnkAliyunOssRepositoryOptions ToAliyunOptions()
    {
        return new SnkAliyunOssRepositoryOptions
        {
            Endpoint = Endpoint,
            BucketName = BucketName,
            AccessKeyId = AccessKeyId,
            AccessKeySecret = AccessKeySecret,
            SecurityToken = SecurityToken,
            Name = $"阿里云OSS:{BucketName}"
        };
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Endpoint))
            throw new ArgumentException("OSS Profile 缺少 Endpoint。", nameof(Endpoint));

        if (string.IsNullOrWhiteSpace(BucketName))
            throw new ArgumentException("OSS Profile 缺少 BucketName。", nameof(BucketName));

        if (string.IsNullOrWhiteSpace(AccessKeyId))
            throw new ArgumentException("OSS Profile 缺少 AccessKeyId。", nameof(AccessKeyId));

        if (string.IsNullOrWhiteSpace(AccessKeySecret))
            throw new ArgumentException("OSS Profile 缺少 AccessKeySecret。", nameof(AccessKeySecret));
    }

    [MenuItem("Tools/资源桶同步/创建默认 OssProfile", false, 201)]
    public static void CreateOrSelectDefaultAsset()
    {
        var profile = AssetDatabase.LoadAssetAtPath<SnkOssProfile>(DefaultAssetPath);
        if (profile == null)
        {
            var directory = Path.GetDirectoryName(DefaultAssetPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            profile = CreateInstance<SnkOssProfile>();
            AssetDatabase.CreateAsset(profile, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Selection.activeObject = profile;
        EditorGUIUtility.PingObject(profile);
    }
}
