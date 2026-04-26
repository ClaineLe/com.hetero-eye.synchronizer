# Changelog

本项目遵循 Keep a Changelog 的记录风格。

## [Unreleased]

### Planned

- 增加腾讯云 COS 仓库实现。
- 增加华为云 OBS 仓库实现。
- 进一步整理公开 API 的命名空间和 package metadata。

## [0.1.0] - 2026-04-26

### Added

- 新增 `SnkSynchronizer`，负责生成同步预览并应用同步计划。
- 新增本地文件夹仓库实现 `SnkLocalRepository`。
- 新增阿里云 OSS 仓库实现 `SnkAliyunOssRepository`。
- 新增 `SnkOssProfile`，用于在 Editor 中配置 OSS 账号信息。
- 新增 Unity EditorWindow，用于选择本地目录、OSS 仓库、渠道、版本、平台和模式。
- 新增 Diff 列表，支持查看新增、更新、删除、跳过文件。
- 新增逐文件进度显示。
- 新增 1 到 5 个并发写入任务，默认并发数为 4。
- 新增严格 MD5 对比策略，本地使用文件 MD5，OSS 使用 ETag。
- 新增 README、CHANGELOG 和 MIT LICENSE。

### Changed

- 将原 `BucketSynchronizer` 归档为更通用的 `Synchronizer` 设计。
- 将同步方向收敛为“左侧仓库同步到右侧仓库”，降低 UI 和行为复杂度。
- 移除同步策略开关，目标端必须提供可对比的 MD5 信息。

