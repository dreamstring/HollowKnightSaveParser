using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HollowKnightSaveParser.Services;
using Application = System.Windows.Application;

namespace HollowKnightSaveParser.Models
{
    // 单个备份文件信息
    public class BackupFileInfo : ObservableObject
    {
        private BackupTagManager? _tagManager;

        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public DateTime CreatedTime { get; set; }
        public long FileSize { get; set; }
        public string FormattedFileSize { get; set; } = string.Empty;
        public BackupType BackupType { get; set; }

        public string CustomTag
        {
            get
            {
                if (_tagManager != null)
                {
                    var tag = _tagManager.GetTag(FileName);
                    System.Diagnostics.Debug.WriteLine(
                        $"BackupFileInfo.CustomTag - FileName: '{FileName}', Tag: '{tag}'");
                    return tag ?? string.Empty;
                }
                System.Diagnostics.Debug.WriteLine(
                    $"BackupFileInfo.CustomTag - FileName: '{FileName}', TagManager 为空");
                return string.Empty;
            }
        }

        // 供外部（SaveFileInfo）注入 TagManager
        public void SetTagManager(BackupTagManager? tagManager)
        {
            _tagManager = tagManager;
            System.Diagnostics.Debug.WriteLine(
                $"BackupFileInfo.SetTagManager - FileName: '{FileName}', TagManager: {(tagManager != null ? "已设置" : "为空")}");
            // 初次设置时就刷新显示
            RefreshLocalizedProperties();
        }

        public string DisplayName => GenerateBackupDisplayName(FileName, CreatedTime, BackupType, CustomTag);
        public string DetailedDisplayName => $"{DisplayName} - {FormattedFileSize}";

        public void RefreshLocalizedProperties()
        {
            OnPropertyChanged(nameof(CustomTag));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(DetailedDisplayName));
        }

        private string GenerateBackupDisplayName(string fileName, DateTime createdTime, BackupType backupType, string customTag)
        {
            var timeStr = createdTime.ToString("MM-dd HH:mm");

            if (!string.IsNullOrWhiteSpace(customTag))
                return $"{timeStr} [{customTag}]";

            var fileNameTag = ExtractCustomTag(fileName);
            if (!string.IsNullOrWhiteSpace(fileNameTag))
                return $"{timeStr} [{fileNameTag}]";

            var typeStr = backupType switch
            {
                BackupType.Auto => GetString("BackupTypeAuto"),
                BackupType.BeforeRestore => GetString("BackupTypeBeforeRestore"),
                BackupType.Timestamped => "",
                BackupType.Manual => GetString("BackupTypeManual"),
                _ => GetString("BackupTypeDefault")
            };

            return string.IsNullOrEmpty(typeStr) ? timeStr : $"{timeStr} ({typeStr})";
        }

        private string ExtractCustomTag(string fileName)
        {
            var unifiedBackupRegex = new Regex(
                @"^user(?<slot>\d+)\.(?<stamp>\d{8}_\d{6})(?:_(?<tag>[^.]+))?\.dat\.bak$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var m = unifiedBackupRegex.Match(fileName);
            if (m.Success && m.Groups["tag"].Success)
            {
                var tag = m.Groups["tag"].Value.Trim();
                if (!string.IsNullOrEmpty(tag))
                    return tag;
            }

            // 兼容格式：user1_20250909_151651_tag.dat / json
            var alt = Regex.Match(fileName,
                @"^user\d+_\d{8}_\d{6}_(.+?)\.(dat|json)$",
                RegexOptions.IgnoreCase);
            if (alt.Success)
                return alt.Groups[1].Value.Trim();

            // 兼容：user1.tag.dat.bak（排除纯时间戳）
            var simple = Regex.Match(fileName,
                @"^user\d+\.(.+?)\.dat\.bak$",
                RegexOptions.IgnoreCase);
            if (simple.Success)
            {
                var maybe = simple.Groups[1].Value.Trim();
                if (!Regex.IsMatch(maybe, @"^\d{8}_\d{6}$"))
                    return maybe;
            }

            return string.Empty;
        }

        private static string GetString(string key)
        {
            try
            {
                return Application.Current.FindResource(key) as string ?? key;
            }
            catch
            {
                return key;
            }
        }
    }

    public enum BackupType
    {
        Manual,
        Auto,
        Timestamped,
        BeforeRestore
    }

    public class SaveFileInfo : ObservableObject
    {
        private BackupTagManager? _tagManager;

        // 统一备份文件命名正则（含 tag）
        private static readonly Regex UnifiedBackupRegex =
            new(@"^user(?<slot>\d+)\.(?<stamp>\d{8}_\d{6})(?:_(?<tag>[^.]+))?\.dat\.bak$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // 仅用于解析槽位
        private static readonly Regex SlotRegex =
            new(@"^user(?<slot>\d+)\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public int SlotNumber { get; set; }
        public string BaseName { get; set; } = string.Empty;
        public string DatFilePath { get; set; } = string.Empty;
        public string JsonFilePath { get; set; } = string.Empty;

        public List<string> RelatedFiles { get; set; } = new();

        private ObservableCollection<BackupFileInfo> _backupVersions = new();
        public ObservableCollection<BackupFileInfo> BackupVersions
        {
            get => _backupVersions;
            set => SetProperty(ref _backupVersions, value);
        }

        public bool HasDatFile => !string.IsNullOrEmpty(DatFilePath) && File.Exists(DatFilePath);
        public bool HasJsonFile => !string.IsNullOrEmpty(JsonFilePath) && File.Exists(JsonFilePath);

        public bool CanBackup => HasDatFile;
        public bool CanRestore => HasBackupFiles;
        public bool HasBackups => BackupCount > 0;

        public int BackupCount
        {
            get
            {
                var categories = CategorizedRelatedFiles;
                return categories["backup"].Count;
            }
        }

        // 设置 TagManager（核心：订阅事件 + 刷新）
        public void SetTagManager(BackupTagManager tagManager)
        {
            if (_tagManager != null)
                _tagManager.TagChanged -= OnGlobalTagChanged;

            _tagManager = tagManager;
            _tagManager.TagChanged -= OnGlobalTagChanged; // 防重复
            _tagManager.TagChanged += OnGlobalTagChanged;

            RefreshBackupVersions(); // 初次加载应用标签
        }

        private bool FileBelongsToCurrentSlot(string fileName)
        {
            var m = SlotRegex.Match(fileName);
            if (!m.Success) return false;
            if (!int.TryParse(m.Groups["slot"].Value, out var slot)) return false;
            return slot == SlotNumber;
        }

        private void OnGlobalTagChanged(string fileName, string newTag)
        {
            // 精确判断是否属于此槽位
            if (!fileName.StartsWith($"user{SlotNumber}.", StringComparison.OrdinalIgnoreCase))
                return;

            // UI 线程保障
            void DoUpdate()
            {
                var target = BackupVersions.FirstOrDefault(b =>
                    b.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));

                if (target != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[TagChanged->Backup Refresh] {fileName} -> {newTag}");
                    target.RefreshLocalizedProperties();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[TagChanged->Not Found, Refresh All] {fileName}");
                    RefreshBackupVersions();
                }
            }

            if (Application.Current?.Dispatcher != null &&
                !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(DoUpdate);
            }
            else
            {
                DoUpdate();
            }
        }

        public void RefreshBackupVersions()
        {
            BackupVersions.Clear();

            if (string.IsNullOrEmpty(DirectoryPath) || !Directory.Exists(DirectoryPath))
                return;

            var categories = CategorizedRelatedFiles;
            var backupFiles = categories["backup"]
                .Select(filePath =>
                    Path.IsPathRooted(filePath)
                        ? filePath
                        : Path.Combine(DirectoryPath, filePath))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();

            foreach (var backupFile in backupFiles)
            {
                try
                {
                    var backupInfo = CreateBackupFileInfo(backupFile);
                    BackupVersions.Add(backupInfo);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"无法加载备份文件 {backupFile}: {ex.Message}");
                }
            }

            OnPropertyChanged(nameof(HasBackups));
            OnPropertyChanged(nameof(BackupCount));
            OnPropertyChanged(nameof(CanRestore));
        }

        private BackupFileInfo CreateBackupFileInfo(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            var fileName = Path.GetFileName(filePath);
            var createdTime = ParseTimestampFromFileName(fileName) ?? fileInfo.LastWriteTime;

            var backupInfo = new BackupFileInfo
            {
                FilePath = filePath,
                FileName = fileName,
                CreatedTime = createdTime,
                FileSize = fileInfo.Length,
                FormattedFileSize = FormatFileSize(fileInfo.Length),
                BackupType = DetermineBackupType(fileName)
            };

            // 注入 TagManager（需要获取自定义标签）
            backupInfo.SetTagManager(_tagManager);
            return backupInfo;
        }

        // 旧的单文件刷新（现在事件驱动仍可保留）
        public void RefreshBackupTag(string fileName)
        {
            var backup = BackupVersions.FirstOrDefault(b => b.FileName == fileName);
            if (backup != null)
                backup.RefreshLocalizedProperties();
        }

        public void NotifyBackupVersionsChanged() => OnPropertyChanged(nameof(BackupVersions));

        private DateTime? ParseTimestampFromFileName(string fileName)
        {
            try
            {
                var m = UnifiedBackupRegex.Match(fileName);
                if (m.Success)
                {
                    var stamp = m.Groups["stamp"].Value; // yyyyMMdd_HHmmss
                    if (DateTime.TryParseExact(stamp, "yyyyMMdd_HHmmss", null,
                            System.Globalization.DateTimeStyles.None, out var ts))
                        return ts;
                }

                // 兼容旧格式 user1_20250919.235357.dat / json
                var legacy = Regex.Match(fileName,
                    @"^user\d+_(\d{8})\.(\d{6})\.(dat|json)$", RegexOptions.IgnoreCase);
                if (legacy.Success)
                {
                    var stampStr = legacy.Groups[1].Value + "_" + legacy.Groups[2].Value;
                    if (DateTime.TryParseExact(stampStr, "yyyyMMdd_HHmmss", null,
                            System.Globalization.DateTimeStyles.None, out var ts2))
                        return ts2;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"解析时间戳失败 {fileName}: {ex.Message}");
            }

            return null;
        }

        private BackupType DetermineBackupType(string fileName)
        {
            if (fileName.Contains("before_restore", StringComparison.OrdinalIgnoreCase))
                return BackupType.BeforeRestore;
            if (fileName.Contains("auto", StringComparison.OrdinalIgnoreCase))
                return BackupType.Auto;

            if (UnifiedBackupRegex.IsMatch(fileName) ||
                Regex.IsMatch(fileName, @"user\d+_[\d\.]+\.(dat|json)$", RegexOptions.IgnoreCase))
                return BackupType.Timestamped;

            return BackupType.Manual;
        }

        // （可删除：与 BackupFileInfo 的显示逻辑重复；保留不调用）
        private string GenerateBackupDisplayName(string fileName, DateTime createdTime)
        {
            var timeStr = createdTime.ToString("MM-dd HH:mm");
            var type = DetermineBackupType(fileName);
            var customTag = ExtractCustomTag(fileName);

            var typeStr = type switch
            {
                BackupType.Auto => GetString("BackupTypeAuto"),
                BackupType.BeforeRestore => GetString("BackupTypeBeforeRestore"),
                BackupType.Timestamped => "",
                BackupType.Manual => GetString("BackupTypeManual"),
                _ => GetString("BackupTypeDefault")
            };

            var parts = new List<string> { timeStr };

            if (!string.IsNullOrWhiteSpace(customTag))
                parts.Add($"[{customTag}]");
            else if (!string.IsNullOrEmpty(typeStr))
                parts.Add($"({typeStr})");

            return string.Join(" ", parts);
        }

        private string ExtractCustomTag(string fileName)
        {
            var m = UnifiedBackupRegex.Match(fileName);
            if (m.Success && m.Groups["tag"].Success)
            {
                var tag = m.Groups["tag"].Value.Trim();
                if (!string.IsNullOrEmpty(tag))
                    return tag;
            }

            var alt = Regex.Match(fileName,
                @"^user\d+_\d{8}_\d{6}_(.+?)\.(dat|json)$",
                RegexOptions.IgnoreCase);
            if (alt.Success)
                return alt.Groups[1].Value.Trim();

            var simple = Regex.Match(fileName,
                @"^user\d+\.(.+?)\.dat\.bak$",
                RegexOptions.IgnoreCase);
            if (simple.Success)
            {
                var maybe = simple.Groups[1].Value.Trim();
                if (!Regex.IsMatch(maybe, @"^\d{8}_\d{6}$"))
                    return maybe;
            }

            return string.Empty;
        }

        public string GenerateBeforeRestoreBackupPath()
        {
            if (!HasDatFile) return string.Empty;
            return Path.Combine(DirectoryPath, $"{BaseName}.before_restore.bak");
        }

        public void RefreshLocalizedProperties()
        {
            foreach (var backup in BackupVersions)
                backup.RefreshLocalizedProperties();

            OnPropertyChanged(nameof(FileStatusText));
            OnPropertyChanged(nameof(FileTypeDisplayText));
            OnPropertyChanged(nameof(DetailedFileInfo));
            OnPropertyChanged(nameof(DetailedFileTypeInfo));
            OnPropertyChanged(nameof(FormattedLastModified));
            OnPropertyChanged(nameof(DatToJsonButtonText));
            OnPropertyChanged(nameof(JsonToDatButtonText));
            OnPropertyChanged(nameof(ToolTipText));
        }

        public string DisplayFileName
        {
            get
            {
                if (HasDatFile && HasJsonFile) return BaseName;
                if (HasDatFile) return Path.GetFileName(DatFilePath);
                if (HasJsonFile) return Path.GetFileName(JsonFilePath);
                return BaseName;
            }
        }

        public Dictionary<string, List<string>> CategorizedRelatedFiles
        {
            get
            {
                var categories = new Dictionary<string, List<string>>
                {
                    ["standard"] = new(),
                    ["mod"] = new(),
                    ["backup"] = new(),
                    ["other"] = new()
                };

                foreach (var file in RelatedFiles)
                {
                    var fileName = file.ToLowerInvariant();

                    if (Regex.IsMatch(fileName, @"^user\d+\.(dat|json)$"))
                        categories["standard"].Add(file);
                    else if (fileName.Contains("modded"))
                        categories["mod"].Add(file);
                    else if (fileName.Contains("bak") ||
                             fileName.Contains("backup") ||
                             UnifiedBackupRegex.IsMatch(file) ||
                             Regex.IsMatch(fileName, @"user\d+\.\d{8}_\d{6}_.*\.dat\.bak$", RegexOptions.IgnoreCase) ||
                             Regex.IsMatch(fileName, @"user\d+_[\d\.]+\.(dat|json)$", RegexOptions.IgnoreCase) ||
                             Regex.IsMatch(fileName, @"user\d+\.before_restore\.bak$", RegexOptions.IgnoreCase))
                        categories["backup"].Add(file);
                    else
                        categories["other"].Add(file);
                }

                return categories;
            }
        }

        private static string GetString(string key)
        {
            try
            {
                return Application.Current.FindResource(key) as string ?? key;
            }
            catch
            {
                return key;
            }
        }

        public string FileStatusText
        {
            get
            {
                if (HasDatFile && HasJsonFile) return GetString("DatPlusJson");
                if (HasDatFile) return GetString("DatOnly");
                if (HasJsonFile) return GetString("JsonOnly");
                return GetString("Error");
            }
        }

        public string FileTypeDisplayText
        {
            get
            {
                if (HasDatFile && HasJsonFile) return GetString("SaveFileDoubleFormat");
                if (HasDatFile) return GetString("SaveFileBinary");
                if (HasJsonFile) return GetString("SaveFileJson");
                return GetString("Error");
            }
        }

        public string DetailedFileInfo
        {
            get
            {
                var info = new List<string>();
                if (HasDatFile)
                    info.Add($"DAT: {FormatFileSize(new FileInfo(DatFilePath).Length)}");
                if (HasJsonFile)
                    info.Add($"JSON: {FormatFileSize(new FileInfo(JsonFilePath).Length)}");
                if (BackupCount > 0)
                    info.Add($"{GetString("Backup")}: {BackupCount}{GetString("Count")}");
                if (RelatedFiles.Count > BackupCount)
                {
                    var otherCount = RelatedFiles.Count - BackupCount;
                    info.Add($"{GetString("Other")}: {otherCount}{GetString("Count")}");
                }

                return string.Join(" | ", info);
            }
        }

        public string DetailedFileTypeInfo
        {
            get
            {
                var categories = CategorizedRelatedFiles;
                var info = new List<string>();
                if (HasDatFile || HasJsonFile)
                {
                    var saveType = HasDatFile && HasJsonFile ? "DAT+JSON" : HasDatFile ? "DAT" : "JSON";
                    info.Add($"{GetString("SaveFile")}: {saveType}");
                }

                if (categories["mod"].Count > 0)
                    info.Add($"{GetString("ModData")}: {string.Join(", ", categories["mod"])}");
                if (categories["backup"].Count > 0)
                    info.Add($"{GetString("BackupFiles")}: {categories["backup"].Count}{GetString("Count")}");
                return string.Join(" | ", info);
            }
        }

        public long FileSize
        {
            get
            {
                if (HasDatFile && File.Exists(DatFilePath))
                    return new FileInfo(DatFilePath).Length;
                if (HasJsonFile && File.Exists(JsonFilePath))
                    return new FileInfo(JsonFilePath).Length;
                return 0;
            }
        }

        public string FormattedFileSize => FormatFileSize(FileSize);

        private static string FormatFileSize(long size)
        {
            if (size < 1024) return $"{size} B";
            if (size < 1024 * 1024) return $"{size / 1024.0:F1} KB";
            return $"{size / (1024.0 * 1024.0):F1} MB";
        }

        public DateTime LastModified
        {
            get
            {
                DateTime datTime = HasDatFile ? File.GetLastWriteTime(DatFilePath) : DateTime.MinValue;
                DateTime jsonTime = HasJsonFile ? File.GetLastWriteTime(JsonFilePath) : DateTime.MinValue;
                return datTime > jsonTime ? datTime : jsonTime;
            }
        }

        public string FormattedLastModified =>
            LastModified == DateTime.MinValue ? GetString("Unknown") : LastModified.ToString("yyyy-MM-dd HH:mm:ss");

        public bool CanConvertToJson => HasDatFile;
        public bool CanConvertToDat => HasJsonFile;

        public string DatToJsonButtonText => HasJsonFile ? GetString("UpdateJson") : GetString("ConvertToJson");
        public string JsonToDatButtonText => HasDatFile ? GetString("UpdateDat") : GetString("ConvertToDat");

        public string ToolTipText
        {
            get
            {
                var tooltip = new List<string>
                {
                    $"{GetString("Slot")}: {SlotNumber}",
                    $"{GetString("Status")}: {FileStatusText}",
                    $"{GetString("Size")}: {FormattedFileSize}",
                    $"{GetString("Modified")}: {FormattedLastModified}"
                };

                if (HasDatFile)
                    tooltip.Add($"{GetString("DatFile")}: {Path.GetFileName(DatFilePath)}");
                if (HasJsonFile)
                    tooltip.Add($"{GetString("JsonFile")}: {Path.GetFileName(JsonFilePath)}");

                var categories = CategorizedRelatedFiles;
                if (categories["mod"].Count > 0)
                {
                    tooltip.Add($"{GetString("ModDataFiles")}:");
                    foreach (var modFile in categories["mod"])
                        tooltip.Add($"  • {modFile}");
                }

                if (categories["backup"].Count > 0)
                    tooltip.Add($"{GetString("BackupFiles")}: {string.Join(", ", categories["backup"])}");
                if (categories["other"].Count > 0)
                    tooltip.Add($"{GetString("OtherFiles")}: {string.Join(", ", categories["other"])}");

                return string.Join("\n", tooltip);
            }
        }

        public bool IsValid
        {
            get
            {
                try
                {
                    if (HasDatFile && !File.Exists(DatFilePath)) return false;
                    if (HasJsonFile && !File.Exists(JsonFilePath)) return false;
                    if (HasDatFile && new FileInfo(DatFilePath).Length == 0) return false;
                    if (HasJsonFile && new FileInfo(JsonFilePath).Length == 0) return false;
                    return HasDatFile || HasJsonFile;
                }
                catch
                {
                    return false;
                }
            }
        }

        public string PrimaryFilePath
        {
            get
            {
                if (HasDatFile) return DatFilePath;
                if (HasJsonFile) return JsonFilePath;
                return string.Empty;
            }
        }

        public string DirectoryPath
        {
            get
            {
                var primaryPath = PrimaryFilePath;
                return string.IsNullOrEmpty(primaryPath)
                    ? string.Empty
                    : Path.GetDirectoryName(primaryPath) ?? string.Empty;
            }
        }

        public bool HasModData => CategorizedRelatedFiles["mod"].Count > 0;

        public string? ModDataFilePath
        {
            get
            {
                var modFiles = CategorizedRelatedFiles["mod"];
                return modFiles.FirstOrDefault(f => f.Contains("modded.json"));
            }
        }

        public bool HasBackupFiles
        {
            get
            {
                var categories = CategorizedRelatedFiles;
                return categories["backup"].Count > 0;
            }
        }

        public string? LatestBackupFilePath
        {
            get
            {
                if (!HasBackupFiles) return null;
                var backupFiles = CategorizedRelatedFiles["backup"]
                    .Where(f => f.EndsWith(".dat.bak", StringComparison.OrdinalIgnoreCase))
                    .Select(f => Path.Combine(DirectoryPath, f))
                    .Where(File.Exists)
                    .OrderByDescending(File.GetLastWriteTime)
                    .ToList();
                return backupFiles.FirstOrDefault();
            }
        }

        public string GenerateBackupFilePath()
        {
            if (!HasDatFile) return string.Empty;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.ChangeExtension(DatFilePath, $".{timestamp}.dat.bak");
        }
    }
}