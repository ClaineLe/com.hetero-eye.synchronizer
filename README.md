# Hetero Eye Synchronizer

Hetero Eye Synchronizer 是一个 Unity Editor 同步工具，用于把一个仓库的文件严格同步到另一个仓库。

当前内置实现：

- 本地文件夹仓库。
- 阿里云 OSS 仓库。
- 本地到 OSS 的预览与应用。
- 基于 MD5 的严格差异比较。
- Diff 列表、逐文件进度、并发上传。

设计目标是让“同步逻辑”和“仓库 IO 实现”解耦。同步器只关心读、写、枚举、删除这些能力，不关心右侧仓库到底是 OSS、COS、OBS，还是以后别的 CDN/对象存储。

## 安装

把包放到项目的 `Packages/com.hetero-eye.synchronizer` 目录即可作为 Embedded Package 使用。

如果发布到 Git 仓库，也可以通过 Unity Package Manager 使用 Git URL 安装：

```text
https://github.com/your-name/com.hetero-eye.synchronizer.git
```

## 依赖

- Unity 2019.4 或更高版本。
- 仅 Editor 使用，不包含运行时代码。
- 阿里云 OSS 支持依赖 `Aliyun.OSS.Core.dll`。

注意：同一个 Unity 工程里只应该保留一份 `Aliyun.OSS.Core.dll`。如果工程的 `Assets/Plugins` 下已经有阿里云 OSS SDK，请避免包内和工程内重复导入同名 DLL。

## 快速使用

1. 在 Unity 菜单打开 `Tools/资源桶同步/Snk 同步窗口`。
2. 左侧选择本地根目录。
3. 右侧选择 OSS 仓库，并拖入 `SnkOssProfile`。
4. 选择渠道、版本号、平台、模式。
5. 点击“生成预览”，检查 Diff 列表。
6. 点击“应用预览”，把左侧内容同步到右侧。

当前窗口的同步方向固定为：

```text
本地仓库 -> 云仓库
```

## OssProfile

`SnkOssProfile` 是一个 Editor 用的 `ScriptableObject`，用于保存 OSS 账号信息。

可以通过菜单创建默认资产：

```text
Tools/资源桶同步/创建默认 OssProfile
```

建议把真实 AccessKey 放在团队约定的本地私有配置中，避免把生产账号提交到公开仓库。

## 路径规则

窗口会根据本地根目录扫描可用版本和模式，最终同步路径规则为：

```text
渠道/v版本号/平台/模式
```

例如：

```text
seeg/v1.0.15/douyin/debug
```

左侧实际目录为：

```text
本地根目录/seeg/v1.0.15/douyin/debug
```

右侧实际目录为：

```text
OSS Bucket/远端前缀/seeg/v1.0.15/douyin/debug
```

## 严格 MD5

同步器会对比源端和目标端的 MD5：

- 本地仓库使用文件内容计算 MD5。
- OSS 仓库使用远端对象的 ETag 作为 MD5。
- MD5 不一致时认为需要更新。
- 目标端多出的文件会被删除，以保证目标端和源端一致。

这个策略要求 OSS ETag 必须等价于文件 MD5。当前 OSS 上传使用普通 `PutObject`，避免 multipart upload 造成 ETag 不再等于 MD5。

## 代码使用

除了 EditorWindow，也可以直接用同步器 API：

```csharp
var source = new SnkLocalRepository(localDirectory);

var target = new SnkAliyunOssRepository(new SnkAliyunOssRepositoryOptions
{
    Endpoint = endpoint,
    BucketName = bucketName,
    AccessKeyId = accessKeyId,
    AccessKeySecret = accessKeySecret,
    Prefix = remotePrefix
});

var synchronizer = new SnkSynchronizer();
var preview = await synchronizer.PreviewAsync(source, target);

await synchronizer.ApplyAsync(
    source,
    target,
    preview,
    new Progress<SnkSyncProgress>(progress =>
    {
        UnityEngine.Debug.Log($"{progress.Path}: {progress.ItemProgress:P0}");
    }));
```

## 扩展仓库

新增云仓库时，不需要改同步算法。实现对应仓库接口即可：

- `SnkRepositoryReader`：枚举、读取、获取文件信息。
- `SnkRepositoryWriter`：写入、删除。
- `SnkRepository`：同时具备读写能力的仓库基类。

例如后续要接入腾讯云 COS 或华为云 OBS，只需要实现各自的 Repository，然后交给 `SnkSynchronizer` 做预览和应用。

## 安全建议

- 不要把真实 AccessKey/Secret 提交到公开仓库。
- 同步前先生成预览，确认删除列表符合预期。
- 首次接入新的 Bucket 或 Prefix 时，建议先使用测试目录验证。

