using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class SnkSynchronizerWindow : EditorWindow
{
    private const string PrefKeyPrefix = "SnkSynchronizerWindow.";
    private const string GameProfileAssetPath = "Assets/Resources/GameProfile.asset";
    private const int ConnectionTimeoutMilliseconds = 20000;
    private const int UploadTimeoutMilliseconds = 600000;
    private const int MaxErrorRetry = 2;
    private const float RepositoryPanelHeight = 190f;
    private const float RepositoryArrowWidth = 48f;
    private const float WindowHorizontalPadding = 22f;
    private const float RelativePathColumnGap = 8f;
    private const float DiffListMinHeight = 260f;
    private const float DiffListRowHeight = 20f;
    private const float DiffListScrollbarWidth = 16f;
    private const float DiffListProgressWidth = 120f;

    private enum CloudRepositoryType
    {
        Oss,
        Cos,
        Obs
    }

    private enum PreviewFilter
    {
        Changed,
        All,
        Creates,
        Updates,
        Deletes,
        Skips
    }

    private string _localRootDirectory = string.Empty;
    private string _syncRelativePath = string.Empty;
    private string _channelName = string.Empty;
    private string _appVersion = string.Empty;
    private int _platformIndex;
    private int _configurationIndex;
    private string[] _availableVersions = Array.Empty<string>();
    private string _lastScannedVersionDirectory = string.Empty;
    private string _lastScannedChannelName = string.Empty;
    private string[] _availableConfigurations = Array.Empty<string>();
    private string _lastScannedConfigurationDirectory = string.Empty;
    private string _remotePrefix = string.Empty;
    private SnkOssProfile _ossProfile;
    private CloudRepositoryType _targetRepositoryType = CloudRepositoryType.Oss;

    private bool _isBusy;
    private SnkSyncPreview _preview;
    private readonly Dictionary<string, float> _rowProgressByPath =
        new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeProgressPaths =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failedProgressPaths =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private Vector2 _previewListScrollPosition;
    private PreviewFilter _previewFilter = PreviewFilter.Changed;
    private static GUIStyle _repositoryArrowStyle;

    private static readonly string[] CloudRepositoryTypeLabels =
    {
        "OSS",
        "COS",
        "OBS"
    };

    private static readonly string[] PlatformLabels =
    {
        "抖音小游戏 (douyin)",
        "WebGL (webgl)",
        "Android (android)",
        "iOS (ios)",
        "PC (pc)",
        "微信小游戏 (wechat)",
        "快手小游戏 (kuaishou)",
        "TapTap 小游戏 (taptap)",
        "233 小游戏 (metaapp)",
        "小游戏宿主 (minihost)"
    };

    private static readonly string[] PlatformPathSegments =
    {
        "douyin",
        "webgl",
        "android",
        "ios",
        "pc",
        "wechat",
        "kuaishou",
        "taptap",
        "metaapp",
        "minihost"
    };

    private static readonly string[] ConfigurationLabels =
    {
        "Release (release)",
        "Debug (debug)",
        "Alpha (alpha)",
        "Develop (develop)"
    };

    private static readonly string[] ConfigurationPathSegments =
    {
        "release",
        "debug",
        "alpha",
        "develop"
    };

    private static readonly string[] PreviewFilterLabels =
    {
        "变更",
        "全部",
        "新增",
        "更新",
        "删除",
        "跳过"
    };

    [MenuItem("Tools/资源桶同步/Snk 同步窗口")]
    private static void Open()
    {
        var window = GetWindow<SnkSynchronizerWindow>("Snk 桶同步");
        window.minSize = new Vector2(900f, 640f);
        window.Show();
    }

    private void OnEnable()
    {
        _platformIndex = GetDefaultPlatformIndex();
        _configurationIndex = FindConfigurationIndex("debug");
        TryLoadGameProfileDefaults();

        _localRootDirectory = EditorPrefs.GetString(
            PrefKey(nameof(_localRootDirectory)),
            EditorPrefs.GetString(PrefKey("_localDirectory"), _localRootDirectory));
        var legacyRelativePath = EditorPrefs.GetString(PrefKey("_syncRelativePath"), string.Empty);
        var legacyLocalRelativePath = EditorPrefs.GetString(PrefKey("_localRelativePath"), legacyRelativePath);
        var legacyRemoteRelativePath = EditorPrefs.GetString(PrefKey("_remoteRelativePath"), legacyLocalRelativePath);
        ApplyRelativePathDefaults(EditorPrefs.GetString(PrefKey(nameof(_syncRelativePath)), legacyLocalRelativePath));
        _channelName = EditorPrefs.GetString(PrefKey(nameof(_channelName)), _channelName);
        _appVersion = EditorPrefs.GetString(PrefKey(nameof(_appVersion)), _appVersion);
        _platformIndex = ClampIndex(EditorPrefs.GetInt(PrefKey(nameof(_platformIndex)), _platformIndex), PlatformPathSegments.Length);
        _configurationIndex = ClampIndex(EditorPrefs.GetInt(PrefKey(nameof(_configurationIndex)), _configurationIndex), ConfigurationPathSegments.Length);
        _syncRelativePath = GetSyncRelativePath();
        _remotePrefix = EditorPrefs.GetString(
            PrefKey(nameof(_remotePrefix)),
            legacyRemoteRelativePath == legacyLocalRelativePath ? string.Empty : legacyRemoteRelativePath);
        _ossProfile = LoadOssProfile(EditorPrefs.GetString(PrefKey(nameof(_ossProfile)), string.Empty));
        _targetRepositoryType = (CloudRepositoryType)EditorPrefs.GetInt(
            PrefKey(nameof(_targetRepositoryType)),
            (int)_targetRepositoryType);
        RefreshAvailableVersions(false);
        RefreshAvailableConfigurations(true);
    }

    private void OnDisable()
    {
        _syncRelativePath = GetSyncRelativePath();
        EditorPrefs.SetString(PrefKey(nameof(_localRootDirectory)), _localRootDirectory);
        EditorPrefs.SetString(PrefKey(nameof(_syncRelativePath)), _syncRelativePath);
        EditorPrefs.SetString(PrefKey(nameof(_channelName)), _channelName);
        EditorPrefs.SetString(PrefKey(nameof(_appVersion)), _appVersion);
        EditorPrefs.SetInt(PrefKey(nameof(_platformIndex)), _platformIndex);
        EditorPrefs.SetInt(PrefKey(nameof(_configurationIndex)), _configurationIndex);
        EditorPrefs.SetString(PrefKey(nameof(_remotePrefix)), _remotePrefix);
        EditorPrefs.SetString(PrefKey(nameof(_ossProfile)), GetAssetPath(_ossProfile));
        EditorPrefs.SetInt(PrefKey(nameof(_targetRepositoryType)), (int)_targetRepositoryType);
    }

    private void OnGUI()
    {
        DrawTopRepositoryArea();
        EditorGUILayout.Space(8f);
        DrawRelativePathArea();
        EditorGUILayout.Space(8f);
        DrawBottomDiffArea();
    }

    private void DrawTopRepositoryArea()
    {
        EditorGUILayout.LabelField("同步仓库", EditorStyles.boldLabel);
        var panelWidth = GetRepositoryPanelWidth();
        using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandWidth(true)))
        {
            DrawLocalSourcePanel(panelWidth);
            DrawDirectionPanel();
            DrawCloudTargetPanel(panelWidth);
        }
    }

    private float GetRepositoryPanelWidth()
    {
        var availableWidth = Mathf.Max(0f, position.width - WindowHorizontalPadding - RepositoryArrowWidth);
        return Mathf.Max(260f, availableWidth * 0.5f);
    }

    private float GetRelativePathColumnWidth()
    {
        var availableWidth = Mathf.Max(
            0f,
            position.width - WindowHorizontalPadding - RelativePathColumnGap * 3f);
        return Mathf.Max(150f, availableWidth / 4f);
    }

    private static GUIStyle GetRepositoryArrowStyle()
    {
        if (_repositoryArrowStyle != null)
        {
            return _repositoryArrowStyle;
        }

        _repositoryArrowStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 22
        };
        return _repositoryArrowStyle;
    }

    private void DrawLocalSourcePanel(float panelWidth)
    {
        using (new EditorGUILayout.VerticalScope(
                   "HelpBox",
                   GUILayout.Width(panelWidth),
                   GUILayout.MinHeight(RepositoryPanelHeight),
                   GUILayout.ExpandWidth(false)))
        {
            EditorGUILayout.LabelField("左侧：本地仓库", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                _localRootDirectory = EditorGUILayout.TextField("本地根目录", _localRootDirectory);
                if (EditorGUI.EndChangeCheck())
                {
                    RefreshAvailableVersions(true);
                }

                if (GUILayout.Button("粘贴", GUILayout.Width(64f)))
                {
                    SetLocalRootDirectory(EditorGUIUtility.systemCopyBuffer);
                }

                if (GUILayout.Button("选择...", GUILayout.Width(80f)))
                {
                    var selected = EditorUtility.OpenFolderPanel("选择本地目录", GetFolderPanelStartPath(), string.Empty);
                    if (!string.IsNullOrWhiteSpace(selected))
                    {
                        SetLocalRootDirectory(selected);
                    }
                }
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("本地目录", GetEffectiveLocalDirectory());
            }

            var localPathExists = Directory.Exists(GetEffectiveLocalDirectory());
            EditorGUILayout.HelpBox(
                localPathExists
                    ? "同步方向固定为：左侧本地仓库 -> 右侧云仓库。"
                    : "请选择一个存在的本地根目录，并确认同步相对路径存在。",
                localPathExists ? MessageType.Info : MessageType.Warning);
        }
    }

    private static void DrawDirectionPanel()
    {
        using (new EditorGUILayout.VerticalScope(
                   GUILayout.Width(RepositoryArrowWidth),
                   GUILayout.MinHeight(RepositoryPanelHeight),
                   GUILayout.ExpandWidth(false)))
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "→",
                GetRepositoryArrowStyle(),
                GUILayout.Width(RepositoryArrowWidth),
                GUILayout.Height(28f));
            GUILayout.FlexibleSpace();
        }
    }

    private void DrawCloudTargetPanel(float panelWidth)
    {
        using (new EditorGUILayout.VerticalScope(
                   "HelpBox",
                   GUILayout.Width(panelWidth),
                   GUILayout.MinHeight(RepositoryPanelHeight),
                   GUILayout.ExpandWidth(false)))
        {
            EditorGUILayout.LabelField("右侧：云仓库", EditorStyles.boldLabel);
            _targetRepositoryType = (CloudRepositoryType)EditorGUILayout.Popup(
                "仓库类型",
                (int)_targetRepositoryType,
                CloudRepositoryTypeLabels);

            switch (_targetRepositoryType)
            {
                case CloudRepositoryType.Oss:
                    DrawOssProfileField();
                    break;
                case CloudRepositoryType.Cos:
                    EditorGUILayout.HelpBox("COS 仓库还没有实现，目前只能选择 OSS。", MessageType.Warning);
                    break;
                case CloudRepositoryType.Obs:
                    EditorGUILayout.HelpBox("OBS 仓库还没有实现，目前只能选择 OSS。", MessageType.Warning);
                    break;
            }
        }
    }

    private void DrawOssProfileField()
    {
        _ossProfile = (SnkOssProfile)EditorGUILayout.ObjectField(
            "OSS Profile",
            _ossProfile,
            typeof(SnkOssProfile),
            false);

        if (_ossProfile == null)
        {
            EditorGUILayout.HelpBox("请把 Assets/Editor Resources/OssProfile.asset 拖到这里。", MessageType.Warning);
            return;
        }

        DrawEditableOssProfileFields();

        _remotePrefix = EditorGUILayout.TextField(
            "远端前缀",
            SnkPathUtility.NormalizeRelativePath(_remotePrefix));

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.TextField("远端目录", GetDisplayRemoteDirectory());
        }
    }

    private void DrawEditableOssProfileFields()
    {
        EditorGUI.BeginChangeCheck();
        _ossProfile.Endpoint = EditorGUILayout.TextField("访问节点", _ossProfile.Endpoint);
        _ossProfile.BucketName = EditorGUILayout.TextField("远端 Bucket", _ossProfile.BucketName);
        _ossProfile.AccessKeyId = EditorGUILayout.TextField("AccessKeyId", _ossProfile.AccessKeyId);
        _ossProfile.AccessKeySecret = EditorGUILayout.PasswordField("AccessKeySecret", _ossProfile.AccessKeySecret);
        _ossProfile.SecurityToken = EditorGUILayout.PasswordField("SecurityToken", _ossProfile.SecurityToken);

        if (!EditorGUI.EndChangeCheck())
        {
            return;
        }

        EditorUtility.SetDirty(_ossProfile);
        AssetDatabase.SaveAssets();
    }

    private void DrawRelativePathArea()
    {
        EditorGUILayout.LabelField("相对路径", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope("HelpBox"))
        {
            DrawRelativePathSelectors();

            if (_availableVersions.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "未在本地根目录/渠道下扫描到 v* 版本目录。请确认目录类似：E:/nginxService/seeg/v1.0.15。",
                    MessageType.Warning);
            }
            if (_availableConfigurations.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "当前版本/平台下未扫描到可用模式目录。请确认目录类似：E:/nginxService/seeg/v1.0.15/douyin/debug。",
                    MessageType.Warning);
            }

            _syncRelativePath = GetSyncRelativePath();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("生成路径", _syncRelativePath);
            }

            EditorGUILayout.HelpBox(
                "实际同步范围 = 上方仓库目录 + 生成路径。生成规则为：渠道/v版本号/平台/模式。",
                MessageType.Info);
        }
    }

    private void DrawRelativePathSelectors()
    {
        var columnWidth = GetRelativePathColumnWidth();
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawChannelSelectorColumn(columnWidth);
            GUILayout.Space(RelativePathColumnGap);
            DrawVersionSelectorColumn(columnWidth);
            GUILayout.Space(RelativePathColumnGap);
            DrawPlatformSelectorColumn(columnWidth);
            GUILayout.Space(RelativePathColumnGap);
            DrawConfigurationSelectorColumn(columnWidth);
        }
    }

    private void DrawChannelSelectorColumn(float columnWidth)
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(columnWidth), GUILayout.ExpandWidth(false)))
        {
            EditorGUILayout.LabelField("渠道", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            _channelName = EditorGUILayout.TextField(
                NormalizePathSegment(_channelName),
                GUILayout.Width(columnWidth));
            if (EditorGUI.EndChangeCheck())
            {
                RefreshAvailableVersions(true);
            }
        }
    }

    private void DrawVersionSelectorColumn(float columnWidth)
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(columnWidth), GUILayout.ExpandWidth(false)))
        {
            EditorGUILayout.LabelField("版本号", EditorStyles.miniBoldLabel);
            DrawVersionSelectorField(columnWidth);
        }
    }

    private void DrawPlatformSelectorColumn(float columnWidth)
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(columnWidth), GUILayout.ExpandWidth(false)))
        {
            EditorGUILayout.LabelField("平台", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            _platformIndex = EditorGUILayout.Popup(
                ClampIndex(_platformIndex, PlatformLabels.Length),
                PlatformLabels,
                GUILayout.Width(columnWidth));
            if (EditorGUI.EndChangeCheck())
            {
                RefreshAvailableConfigurations(true);
            }
        }
    }

    private void DrawConfigurationSelectorColumn(float columnWidth)
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(columnWidth), GUILayout.ExpandWidth(false)))
        {
            EditorGUILayout.LabelField("模式", EditorStyles.miniBoldLabel);
            DrawConfigurationSelectorField(columnWidth);
        }
    }

    private void DrawVersionSelectorField(float columnWidth)
    {
        EnsureVersionListIsFresh();
        const float refreshButtonWidth = 58f;
        var versionControlWidth = Mathf.Max(80f, columnWidth - refreshButtonWidth - 4f);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (_availableVersions.Length > 0)
            {
                var previousVersion = _appVersion;
                var selectedIndex = FindVersionIndex(_appVersion);
                if (selectedIndex < 0)
                {
                    selectedIndex = 0;
                    _appVersion = _availableVersions[selectedIndex];
                }

                selectedIndex = EditorGUILayout.Popup(
                    selectedIndex,
                    _availableVersions.Select(BuildVersionPathSegment).ToArray(),
                    GUILayout.Width(versionControlWidth));
                _appVersion = _availableVersions[selectedIndex];

                if (!string.Equals(previousVersion, _appVersion, StringComparison.OrdinalIgnoreCase))
                {
                    RefreshAvailableConfigurations(true);
                }
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                _appVersion = EditorGUILayout.TextField(
                    NormalizeAppVersion(_appVersion),
                    GUILayout.Width(versionControlWidth));
                if (EditorGUI.EndChangeCheck())
                {
                    RefreshAvailableConfigurations(true);
                }
            }

            if (GUILayout.Button("刷新", GUILayout.Width(refreshButtonWidth)))
            {
                RefreshAvailableVersions(false);
            }
        }
    }

    private void DrawConfigurationSelectorField(float columnWidth)
    {
        EnsureConfigurationListIsFresh();

        if (_availableConfigurations.Length == 0)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Popup(0, new[] { "无可用模式" }, GUILayout.Width(columnWidth));
            }

            return;
        }

        var selectedPathSegment = GetSelectedConfigurationPathSegment();
        var selectedIndex = FindAvailableConfigurationIndex(selectedPathSegment);
        if (selectedIndex < 0)
        {
            selectedIndex = 0;
            _configurationIndex = FindConfigurationIndex(_availableConfigurations[selectedIndex]);
        }

        selectedIndex = EditorGUILayout.Popup(
            selectedIndex,
            _availableConfigurations.Select(BuildConfigurationLabel).ToArray(),
            GUILayout.Width(columnWidth));
        _configurationIndex = FindConfigurationIndex(_availableConfigurations[selectedIndex]);
    }

    private void DrawBottomDiffArea()
    {
        EditorGUILayout.LabelField("同步操作与 Diff", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(
                   "HelpBox",
                   GUILayout.ExpandWidth(true),
                   GUILayout.ExpandHeight(true)))
        {
            DrawActions();
            EditorGUILayout.Space(6f);
            DrawPreview();
        }
    }

    private void DrawActions()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_isBusy))
            {
                if (GUILayout.Button("生成预览", GUILayout.Height(30f)))
                {
                    PreviewAsync();
                }
            }

            using (new EditorGUI.DisabledScope(_isBusy || _preview == null))
            {
                if (GUILayout.Button("应用预览", GUILayout.Height(30f)))
                {
                    ApplyAsync();
                }
            }
        }
    }

    private void DrawPreview()
    {
        if (_preview == null)
        {
            EditorGUILayout.LabelField("尚未生成预览。");
            DrawEmptyPreviewSpace();
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"新增: {_preview.CreateCount}");
            EditorGUILayout.LabelField($"更新: {_preview.UpdateCount}");
            EditorGUILayout.LabelField($"删除: {_preview.DeleteCount}");
            EditorGUILayout.LabelField($"跳过: {_preview.SkipCount}");
            EditorGUILayout.LabelField($"写入: {FormatBytes(_preview.WriteBytes)}");
        }

        EditorGUILayout.Space(6f);
        DrawPreviewList();
    }

    private void DrawPreviewList()
    {
        _previewFilter = (PreviewFilter)GUILayout.Toolbar((int)_previewFilter, PreviewFilterLabels);
        var items = GetPreviewItems(_preview, _previewFilter).ToList();
        EditorGUILayout.LabelField($"Diff 列表 ({items.Count})", EditorStyles.miniBoldLabel);

        DrawPreviewListHeader();
        var scrollRect = GUILayoutUtility.GetRect(
            0f,
            100000f,
            DiffListMinHeight,
            100000f,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));
        DrawPreviewListScrollView(scrollRect, items);
    }

    private static void DrawEmptyPreviewSpace()
    {
        var rect = GUILayoutUtility.GetRect(
            0f,
            100000f,
            DiffListMinHeight,
            100000f,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));
        GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

        var labelRect = new Rect(rect.x, rect.y + 8f, rect.width, 20f);
        GUI.Label(labelRect, "生成预览后这里会显示 Diff 列表。", EditorStyles.centeredGreyMiniLabel);
    }

    private void DrawPreviewListScrollView(Rect scrollRect, List<SnkSyncItem> items)
    {
        var contentWidth = Mathf.Max(0f, scrollRect.width - DiffListScrollbarWidth);
        var contentHeight = Mathf.Max(scrollRect.height, Mathf.Max(1, items.Count) * DiffListRowHeight);
        var contentRect = new Rect(0f, 0f, contentWidth, contentHeight);

        _previewListScrollPosition = GUI.BeginScrollView(scrollRect, _previewListScrollPosition, contentRect);
        if (items.Count == 0)
        {
            GUI.Label(new Rect(0f, 0f, contentWidth, DiffListRowHeight), "无");
        }
        else
        {
            for (var index = 0; index < items.Count; index++)
            {
                var rowRect = new Rect(0f, index * DiffListRowHeight, contentWidth, DiffListRowHeight);
                DrawPreviewListRow(rowRect, items[index], index);
            }
        }

        GUI.EndScrollView();
    }

    private static void DrawPreviewListHeader()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            GUILayout.Label("类型", EditorStyles.miniBoldLabel, GUILayout.Width(52f));
            GUILayout.Label("进度", EditorStyles.miniBoldLabel, GUILayout.Width(DiffListProgressWidth));
            GUILayout.Label("大小", EditorStyles.miniBoldLabel, GUILayout.Width(88f));
            GUILayout.Label("源 MD5", EditorStyles.miniBoldLabel, GUILayout.Width(96f));
            GUILayout.Label("目标 MD5", EditorStyles.miniBoldLabel, GUILayout.Width(96f));
            GUILayout.Label("路径", EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(true));
        }
    }

    private void DrawPreviewListRow(Rect rowRect, SnkSyncItem item, int index)
    {
        if (index % 2 == 1)
        {
            EditorGUI.DrawRect(rowRect, new Color(0f, 0f, 0f, 0.04f));
        }

        var x = rowRect.x;
        DrawPreviewListCell(ref x, rowRect, FormatChangeType(item.ChangeType), 52f);
        DrawPreviewProgressCell(ref x, rowRect, item);
        DrawPreviewListCell(ref x, rowRect, FormatBytes(item.Size), 88f);
        DrawPreviewListCell(ref x, rowRect, FormatShortMd5(item.SourceEntry?.ContentMd5), 96f);
        DrawPreviewListCell(ref x, rowRect, FormatShortMd5(item.TargetEntry?.ContentMd5), 96f);

        var pathRect = new Rect(x, rowRect.y, Mathf.Max(0f, rowRect.xMax - x), rowRect.height);
        GUI.Label(pathRect, new GUIContent(item.Path, item.Path));
    }

    private void DrawPreviewProgressCell(ref float x, Rect rowRect, SnkSyncItem item)
    {
        var progress = GetRowProgress(item);
        var label = GetRowProgressLabel(item, progress);
        var progressRect = new Rect(
            x + 3f,
            rowRect.y + 2f,
            DiffListProgressWidth - 6f,
            rowRect.height - 4f);
        EditorGUI.ProgressBar(progressRect, progress, label);
        x += DiffListProgressWidth;
    }

    private static void DrawPreviewListCell(ref float x, Rect rowRect, string text, float width)
    {
        GUI.Label(new Rect(x, rowRect.y, width, rowRect.height), text);
        x += width;
    }

    private float GetRowProgress(SnkSyncItem item)
    {
        if (item.ChangeType == SnkSyncChangeType.Skip)
        {
            return 1f;
        }

        var path = SnkPathUtility.NormalizeRelativePath(item.Path);
        return _rowProgressByPath.TryGetValue(path, out var progress)
            ? Mathf.Clamp01(progress)
            : 0f;
    }

    private string GetRowProgressLabel(SnkSyncItem item, float progress)
    {
        var path = SnkPathUtility.NormalizeRelativePath(item.Path);
        if (_failedProgressPaths.Contains(path))
        {
            return "失败";
        }

        if (item.ChangeType == SnkSyncChangeType.Skip)
        {
            return "跳过";
        }

        if (progress >= 0.999f)
        {
            return "完成";
        }

        if (_activeProgressPaths.Contains(path))
        {
            return "处理中";
        }

        return "等待";
    }

    private void ResetRowProgress()
    {
        _rowProgressByPath.Clear();
        _activeProgressPaths.Clear();
        _failedProgressPaths.Clear();
    }

    private void InitializeRowProgress(SnkSyncPreview preview)
    {
        ResetRowProgress();
        if (preview == null)
        {
            return;
        }

        foreach (var item in preview.Creates.Concat(preview.Updates).Concat(preview.Deletes))
        {
            _rowProgressByPath[SnkPathUtility.NormalizeRelativePath(item.Path)] = 0f;
        }

        foreach (var item in preview.Skips)
        {
            _rowProgressByPath[SnkPathUtility.NormalizeRelativePath(item.Path)] = 1f;
        }
    }

    private void UpdateRowProgress(SnkSyncProgress info)
    {
        var path = SnkPathUtility.NormalizeRelativePath(info.Path);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        _rowProgressByPath[path] = Mathf.Clamp01(info.ItemProgress);
        if (info.ItemFailed)
        {
            _failedProgressPaths.Add(path);
            _activeProgressPaths.Remove(path);
            return;
        }

        _failedProgressPaths.Remove(path);
        if (info.ItemCompleted)
        {
            _activeProgressPaths.Remove(path);
        }
        else
        {
            _activeProgressPaths.Add(path);
        }
    }

    private static IEnumerable<SnkSyncItem> GetPreviewItems(SnkSyncPreview preview, PreviewFilter filter)
    {
        switch (filter)
        {
            case PreviewFilter.All:
                return preview.Creates.Concat(preview.Updates).Concat(preview.Deletes).Concat(preview.Skips);
            case PreviewFilter.Creates:
                return preview.Creates;
            case PreviewFilter.Updates:
                return preview.Updates;
            case PreviewFilter.Deletes:
                return preview.Deletes;
            case PreviewFilter.Skips:
                return preview.Skips;
            default:
                return preview.Creates.Concat(preview.Updates).Concat(preview.Deletes);
        }
    }

    private async void PreviewAsync()
    {
        if (!ValidateInputs())
        {
            return;
        }

        try
        {
            _isBusy = true;
            _preview = null;
            ResetRowProgress();
            _previewListScrollPosition = Vector2.zero;
            Repaint();

            var synchronizer = new SnkSynchronizer();
            CreateRepositories(out var source, out var target);
            _preview = await synchronizer.PreviewAsync(source, target);
            InitializeRowProgress(_preview);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Snk 桶同步预览失败", exception.ToString(), "确定");
        }
        finally
        {
            _isBusy = false;
            Repaint();
        }
    }

    private async void ApplyAsync()
    {
        if (_preview == null)
        {
            EditorUtility.DisplayDialog("Snk 桶同步", "请先生成预览。", "确定");
            return;
        }

        if (!ValidateInputs())
        {
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "应用预览",
                BuildApplyConfirmText(_preview),
                "应用",
                "取消"))
        {
            return;
        }

        try
        {
            _isBusy = true;
            InitializeRowProgress(_preview);
            Repaint();

            var synchronizer = new SnkSynchronizer();
            CreateRepositories(out var source, out var target);
            var progress = new Progress<SnkSyncProgress>(info =>
            {
                UpdateRowProgress(info);
                Repaint();
            });

            await synchronizer.ApplyAsync(source, target, _preview, progress);
            _activeProgressPaths.Clear();
            _failedProgressPaths.Clear();
            EditorUtility.DisplayDialog("Snk 桶同步", "应用完成。", "确定");
        }
        catch (Exception exception)
        {
            _activeProgressPaths.Clear();
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Snk 桶同步应用失败", exception.ToString(), "确定");
        }
        finally
        {
            _isBusy = false;
            Repaint();
        }
    }

    private void CreateRepositories(out SnkRepository source, out SnkRepository target)
    {
        source = new SnkLocalRepository(GetEffectiveLocalDirectory());
        target = CreateCloudTargetRepository();
    }

    private SnkRepository CreateCloudTargetRepository()
    {
        switch (_targetRepositoryType)
        {
            case CloudRepositoryType.Oss:
                return new SnkAliyunOssRepository(CreateEffectiveOssOptions());
            case CloudRepositoryType.Cos:
                throw new NotSupportedException("COS 仓库还没有实现。");
            case CloudRepositoryType.Obs:
                throw new NotSupportedException("OBS 仓库还没有实现。");
            default:
                throw new NotSupportedException($"未知云仓库类型：{_targetRepositoryType}");
        }
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(_channelName) || string.IsNullOrWhiteSpace(_appVersion))
        {
            EditorUtility.DisplayDialog("Snk 桶同步", "请先填写渠道和版本号。", "确定");
            return false;
        }

        EnsureConfigurationListIsFresh();
        if (_availableConfigurations.Length == 0)
        {
            EditorUtility.DisplayDialog("Snk 桶同步", "当前版本/平台下没有可用的模式目录。", "确定");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_localRootDirectory) || !Directory.Exists(GetEffectiveLocalDirectory()))
        {
            EditorUtility.DisplayDialog("Snk 桶同步", "请先选择一个存在的本地根目录，并确认同步相对路径存在。", "确定");
            return false;
        }

        if (_targetRepositoryType != CloudRepositoryType.Oss)
        {
            EditorUtility.DisplayDialog("Snk 桶同步", "当前只实现了 OSS 仓库。", "确定");
            return false;
        }

        if (_ossProfile == null)
        {
            EditorUtility.DisplayDialog("Snk 桶同步", "请先拖入一个 OSS Profile。", "确定");
            return false;
        }

        try
        {
            _ossProfile.Validate();
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog("Snk 桶同步", exception.Message, "确定");
            return false;
        }

        return true;
    }

    private static string FormatBytes(long bytes)
    {
        const double kb = 1024d;
        const double mb = kb * 1024d;
        const double gb = mb * 1024d;

        if (bytes >= gb)
        {
            return $"{bytes / gb:0.##} GB";
        }

        if (bytes >= mb)
        {
            return $"{bytes / mb:0.##} MB";
        }

        if (bytes >= kb)
        {
            return $"{bytes / kb:0.##} KB";
        }

        return $"{bytes} B";
    }

    private static string FormatShortMd5(string md5)
    {
        if (string.IsNullOrWhiteSpace(md5))
        {
            return "-";
        }

        return md5.Length <= 8 ? md5 : md5.Substring(0, 8);
    }

    private static string BuildApplyConfirmText(SnkSyncPreview preview)
    {
        return $"新增: {preview.CreateCount}\n更新: {preview.UpdateCount}\n删除: {preview.DeleteCount}\n跳过: {preview.SkipCount}\n\n确认应用这些变更吗？";
    }

    private static string FormatChangeType(SnkSyncChangeType changeType)
    {
        switch (changeType)
        {
            case SnkSyncChangeType.Create:
                return "新增";
            case SnkSyncChangeType.Update:
                return "更新";
            case SnkSyncChangeType.Delete:
                return "删除";
            case SnkSyncChangeType.Skip:
                return "跳过";
            default:
                return changeType.ToString();
        }
    }

    private static string PrefKey(string key)
    {
        return PrefKeyPrefix + key;
    }

    private static string GetAssetPath(UnityEngine.Object asset)
    {
        return asset == null ? string.Empty : AssetDatabase.GetAssetPath(asset);
    }

    private static SnkOssProfile LoadOssProfile(string assetPath)
    {
        return string.IsNullOrWhiteSpace(assetPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<SnkOssProfile>(assetPath);
    }

    private void TryLoadGameProfileDefaults()
    {
        var profile = AssetDatabase.LoadMainAssetAtPath(GameProfileAssetPath);
        if (profile == null)
        {
            return;
        }

        var serializedObject = new SerializedObject(profile);
        _channelName = ReadStringProperty(serializedObject, "ChannelName", _channelName);
        _appVersion = NormalizeAppVersion(ReadStringProperty(serializedObject, "AppVersion", _appVersion));

        var configurationProperty = serializedObject.FindProperty("BuildConfiguration");
        if (configurationProperty != null)
        {
            var configurationIndex = configurationProperty.propertyType == SerializedPropertyType.Enum
                ? configurationProperty.enumValueIndex
                : configurationProperty.intValue;
            _configurationIndex = ClampIndex(configurationIndex, ConfigurationPathSegments.Length);
        }
    }

    private static string ReadStringProperty(SerializedObject serializedObject, string propertyName, string fallback)
    {
        var property = serializedObject.FindProperty(propertyName);
        return property == null || string.IsNullOrWhiteSpace(property.stringValue)
            ? fallback
            : property.stringValue;
    }

    private void ApplyRelativePathDefaults(string relativePath)
    {
        var normalizedPath = SnkPathUtility.NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedPath))
        {
            return;
        }

        var parts = normalizedPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            _channelName = NormalizePathSegment(parts[0]);
        }

        if (parts.Length > 1)
        {
            _appVersion = NormalizeAppVersion(parts[1]);
        }

        if (parts.Length > 2)
        {
            _platformIndex = FindPlatformIndex(parts[2]);
        }

        if (parts.Length > 3)
        {
            _configurationIndex = FindConfigurationIndex(parts[3]);
        }
    }

    private void EnsureVersionListIsFresh()
    {
        var versionDirectory = GetVersionScanDirectory();
        var channelName = NormalizePathSegment(_channelName);
        if (string.Equals(versionDirectory, _lastScannedVersionDirectory, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(channelName, _lastScannedChannelName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        RefreshAvailableVersions(false);
    }

    private void RefreshAvailableVersions(bool selectFirstIfCurrentMissing)
    {
        _lastScannedVersionDirectory = GetVersionScanDirectory();
        _lastScannedChannelName = NormalizePathSegment(_channelName);
        _availableVersions = ScanAvailableVersions(_lastScannedVersionDirectory);

        if (_availableVersions.Length == 0)
        {
            RefreshAvailableConfigurations(true);
            return;
        }

        if (string.IsNullOrWhiteSpace(_appVersion) ||
            (selectFirstIfCurrentMissing && FindVersionIndex(_appVersion) < 0))
        {
            _appVersion = _availableVersions[0];
        }

        RefreshAvailableConfigurations(true);
    }

    private static string[] ScanAvailableVersions(string versionDirectory)
    {
        if (string.IsNullOrWhiteSpace(versionDirectory) || !Directory.Exists(versionDirectory))
        {
            return Array.Empty<string>();
        }

        try
        {
            var versions = Directory
                .GetDirectories(versionDirectory)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name) && name.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                .Select(NormalizeAppVersion)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            versions.Sort(CompareVersionsDescending);
            return versions.ToArray();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"扫描同步版本目录失败：{versionDirectory}\n{exception}");
            return Array.Empty<string>();
        }
    }

    private string GetVersionScanDirectory()
    {
        if (string.IsNullOrWhiteSpace(_localRootDirectory))
        {
            return string.Empty;
        }

        string rootPath;
        try
        {
            rootPath = Path.GetFullPath(_localRootDirectory);
        }
        catch
        {
            return string.Empty;
        }

        var channelName = NormalizePathSegment(_channelName);
        if (!string.IsNullOrEmpty(channelName))
        {
            var channelDirectory = Path.Combine(
                rootPath,
                channelName.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(channelDirectory))
            {
                return channelDirectory;
            }
        }

        return Directory.Exists(rootPath) ? rootPath : string.Empty;
    }

    private void EnsureConfigurationListIsFresh()
    {
        var configurationDirectory = GetConfigurationScanDirectory();
        if (string.Equals(configurationDirectory, _lastScannedConfigurationDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        RefreshAvailableConfigurations(false);
    }

    private void RefreshAvailableConfigurations(bool selectFirstIfCurrentMissing)
    {
        _lastScannedConfigurationDirectory = GetConfigurationScanDirectory();
        _availableConfigurations = ScanAvailableConfigurations(_lastScannedConfigurationDirectory);

        if (_availableConfigurations.Length == 0)
        {
            return;
        }

        if (selectFirstIfCurrentMissing &&
            FindAvailableConfigurationIndex(GetSelectedConfigurationPathSegment()) < 0)
        {
            _configurationIndex = FindConfigurationIndex(_availableConfigurations[0]);
        }
    }

    private static string[] ScanAvailableConfigurations(string configurationDirectory)
    {
        if (string.IsNullOrWhiteSpace(configurationDirectory) || !Directory.Exists(configurationDirectory))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory
                .GetDirectories(configurationDirectory)
                .Select(Path.GetFileName)
                .Select(NormalizePathSegment)
                .Where(configuration => FindKnownConfigurationIndex(configuration) >= 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(FindKnownConfigurationIndex)
                .ToArray();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"扫描同步模式目录失败：{configurationDirectory}\n{exception}");
            return Array.Empty<string>();
        }
    }

    private string GetConfigurationScanDirectory()
    {
        var versionScanDirectory = GetVersionScanDirectory();
        var versionPathSegment = BuildVersionPathSegment(_appVersion);
        if (string.IsNullOrWhiteSpace(versionScanDirectory) || string.IsNullOrWhiteSpace(versionPathSegment))
        {
            return string.Empty;
        }

        return Path.Combine(
            versionScanDirectory,
            versionPathSegment,
            GetSelectedPlatformPathSegment());
    }

    private int FindVersionIndex(string appVersion)
    {
        var normalizedVersion = NormalizeAppVersion(appVersion);
        return Array.FindIndex(
            _availableVersions,
            version => string.Equals(version, normalizedVersion, StringComparison.OrdinalIgnoreCase));
    }

    private int FindAvailableConfigurationIndex(string configuration)
    {
        var normalizedConfiguration = NormalizePathSegment(configuration);
        return Array.FindIndex(
            _availableConfigurations,
            value => string.Equals(value, normalizedConfiguration, StringComparison.OrdinalIgnoreCase));
    }

    private static int CompareVersionsDescending(string left, string right)
    {
        return -CompareVersions(left, right);
    }

    private static int CompareVersions(string left, string right)
    {
        var leftIsVersion = Version.TryParse(left, out var leftVersion);
        var rightIsVersion = Version.TryParse(right, out var rightVersion);
        if (leftIsVersion && rightIsVersion)
        {
            return leftVersion.CompareTo(rightVersion);
        }

        if (leftIsVersion)
        {
            return 1;
        }

        if (rightIsVersion)
        {
            return -1;
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private SnkAliyunOssRepositoryOptions CreateEffectiveOssOptions()
    {
        var options = _ossProfile.ToAliyunOptions();
        options.ObjectPrefix = GetEffectiveOssPrefix();
        options.ConnectionTimeoutMilliseconds = ConnectionTimeoutMilliseconds;
        options.UploadTimeoutMilliseconds = UploadTimeoutMilliseconds;
        options.MaxErrorRetry = MaxErrorRetry;
        options.Name = $"阿里云OSS:{options.BucketName}/{options.ObjectPrefix}";
        return options;
    }

    private string GetEffectiveLocalDirectory()
    {
        if (string.IsNullOrWhiteSpace(_localRootDirectory))
        {
            return string.Empty;
        }

        var rootPath = Path.GetFullPath(_localRootDirectory);
        var relativePath = GetSyncRelativePath();
        return string.IsNullOrEmpty(relativePath)
            ? rootPath
            : Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private string GetEffectiveOssPrefix()
    {
        return SnkPathUtility.NormalizePrefix(SnkPathUtility.CombineKey(_remotePrefix, GetSyncRelativePath()));
    }

    private string GetDisplayRemoteDirectory()
    {
        if (_ossProfile == null)
        {
            return GetEffectiveOssPrefix();
        }

        return SnkPathUtility.CombineKey(_ossProfile.BucketName, GetEffectiveOssPrefix());
    }

    private string GetSyncRelativePath()
    {
        var versionPathSegment = BuildVersionPathSegment(_appVersion);
        return CombineRelativePath(
            _channelName,
            versionPathSegment,
            GetSelectedPlatformPathSegment(),
            GetSelectedConfigurationPathSegment());
    }

    private static string CombineRelativePath(params string[] parts)
    {
        var result = string.Empty;
        foreach (var part in parts)
        {
            var normalized = SnkPathUtility.NormalizeRelativePath(part).Trim('/');
            if (string.IsNullOrEmpty(normalized))
            {
                continue;
            }

            result = string.IsNullOrEmpty(result) ? normalized : $"{result}/{normalized}";
        }

        return result;
    }

    private static string NormalizePathSegment(string value)
    {
        return SnkPathUtility.NormalizeRelativePath(value).Trim('/');
    }

    private static string NormalizeAppVersion(string value)
    {
        var normalized = NormalizePathSegment(value);
        return normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring(1)
            : normalized;
    }

    private static string BuildVersionPathSegment(string appVersion)
    {
        var normalized = NormalizeAppVersion(appVersion);
        return string.IsNullOrEmpty(normalized) ? string.Empty : $"v{normalized}";
    }

    private string GetSelectedPlatformPathSegment()
    {
        return PlatformPathSegments[ClampIndex(_platformIndex, PlatformPathSegments.Length)];
    }

    private string GetSelectedConfigurationPathSegment()
    {
        return ConfigurationPathSegments[ClampIndex(_configurationIndex, ConfigurationPathSegments.Length)];
    }

    private static string BuildConfigurationLabel(string pathSegment)
    {
        var index = FindKnownConfigurationIndex(pathSegment);
        return index >= 0 ? ConfigurationLabels[index] : pathSegment;
    }

    private static int GetDefaultPlatformIndex()
    {
#if MINIGAME_SUBPLATFORM_WEIXIN
        return FindPlatformIndex("wechat");
#elif MINIGAME_SUBPLATFORM_DOUYIN
        return FindPlatformIndex("douyin");
#elif MINIGAME_SUBPLATFORM_KUAISHOU
        return FindPlatformIndex("kuaishou");
#elif MINIGAME_SUBPLATFORM_TAPTAP
        return FindPlatformIndex("taptap");
#elif MINIGAME_SUBPLATFORM_METAAPP233
        return FindPlatformIndex("metaapp");
#elif MINIGAME_SUBPLATFORM_MINIHOST
        return FindPlatformIndex("minihost");
#else
        var activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
        if (string.Equals(activeBuildTarget, "Android", StringComparison.OrdinalIgnoreCase))
        {
            return FindPlatformIndex("android");
        }

        if (string.Equals(activeBuildTarget, "iOS", StringComparison.OrdinalIgnoreCase))
        {
            return FindPlatformIndex("ios");
        }

        if (string.Equals(activeBuildTarget, "WebGL", StringComparison.OrdinalIgnoreCase))
        {
            return FindPlatformIndex("webgl");
        }

        if (string.Equals(activeBuildTarget, "MiniGame", StringComparison.OrdinalIgnoreCase))
        {
            return FindPlatformIndex("douyin");
        }

        return FindPlatformIndex("douyin");
#endif
    }

    private static int FindPlatformIndex(string pathSegment)
    {
        return FindPathSegmentIndex(PlatformPathSegments, pathSegment, 0);
    }

    private static int FindConfigurationIndex(string pathSegment)
    {
        var index = FindKnownConfigurationIndex(pathSegment);
        return index >= 0 ? index : 1;
    }

    private static int FindKnownConfigurationIndex(string pathSegment)
    {
        var normalized = NormalizePathSegment(pathSegment);
        return Array.FindIndex(
            ConfigurationPathSegments,
            value => string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static int FindPathSegmentIndex(string[] values, string pathSegment, int fallback)
    {
        var normalized = NormalizePathSegment(pathSegment);
        var index = Array.FindIndex(
            values,
            value => string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : ClampIndex(fallback, values.Length);
    }

    private static int ClampIndex(int index, int length)
    {
        if (length <= 0)
        {
            return 0;
        }

        return Mathf.Clamp(index, 0, length - 1);
    }

    private void SetLocalRootDirectory(string path)
    {
        _localRootDirectory = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);
        RefreshAvailableVersions(true);
        GUI.FocusControl(null);
        Repaint();
    }

    private string GetFolderPanelStartPath()
    {
        if (!string.IsNullOrWhiteSpace(_localRootDirectory) && Directory.Exists(_localRootDirectory))
        {
            return _localRootDirectory;
        }

        return Directory.Exists(Application.dataPath)
            ? Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
