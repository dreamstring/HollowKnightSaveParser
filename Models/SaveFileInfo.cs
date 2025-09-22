using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Application = System.Windows.Application;

namespace HollowKnightSaveParser.Models
{
    // 备份文件信息类
public class BackupFileInfo : ObservableObject
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime CreatedTime { get; set; }
    public long FileSize { get; set; }
    public string FormattedFileSize { get; set; } = string.Empty;
    public BackupType BackupType { get; set; }
    
    // 移除原来的 DisplayName 属性，改为动态计算
    public string DisplayName => GenerateBackupDisplayName(FileName, CreatedTime, BackupType);
    public string DetailedDisplayName => $"{DisplayName} - {FormattedFileSize}";

    // 添加刷新方法
    public void RefreshLocalizedProperties()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DetailedDisplayName));
    }

    // 将生成显示名称的逻辑移到这里
    private string GenerateBackupDisplayName(string fileName, DateTime createdTime, BackupType backupType)
    {
        var timeStr = createdTime.ToString("MM-dd HH:mm");
        var customTag = ExtractCustomTag(fileName);

        var typeStr = backupType switch
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

    // 复制 ExtractCustomTag 方法到这里
    private string ExtractCustomTag(string fileName)
    {
        var unifiedBackupRegex = new Regex(@"^user(?<slot>\d+)\.(?<stamp>\d{8}_\d{6})(?:_(?<tag>[^.]+))?\.dat\.bak$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
            
        var m = unifiedBackupRegex.Match(fileName);
        if (m.Success && m.Groups["tag"].Success)
        {
            var tag = m.Groups["tag"].Value.Trim();
            if (!string.IsNullOrEmpty(tag))
                return tag;
        }

        // 其他兼容性逻辑...
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
        // 新增：统一解析正则  
        private static readonly Regex UnifiedBackupRegex =
            new(@"^user(?<slot>\d+)\.(?<stamp>\d{8}_\d{6})(?:_(?<tag>[^.]+))?\.dat\.bak$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        public void RefreshBackupVersions()
        {
            BackupVersions.Clear();

            if (string.IsNullOrEmpty(DirectoryPath) || !Directory.Exists(DirectoryPath))
                return;

            var categories = CategorizedRelatedFiles;
            var backupFiles = categories["backup"]
                .Select(filePath =>
                {
                    return Path.IsPathRooted(filePath)
                        ? filePath
                        : Path.Combine(DirectoryPath, filePath);
                })
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

            return new BackupFileInfo
            {
                FilePath = filePath,
                FileName = fileName,
                CreatedTime = createdTime,
                FileSize = fileInfo.Length,
                FormattedFileSize = FormatFileSize(fileInfo.Length),
                BackupType = DetermineBackupType(fileName)
            };
        }

        // 使用统一正则解析时间戳 
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

                // 保留旧格式（例如 user1_20250919.235357.dat / json）：
                var legacy = Regex.Match(fileName, @"^user\d+_(\d{8})\.(\d{6})\.(dat|json)$", RegexOptions.IgnoreCase);
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

        private string GenerateBackupDisplayName(string fileName, DateTime createdTime)
        {
            var timeStr = createdTime.ToString("MM-dd HH:mm");
            var type = DetermineBackupType(fileName);
            var customTag = ExtractCustomTag(fileName); // 会返回空或 tag

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

        // 使用统一正则提取 tag 
        private string ExtractCustomTag(string fileName)
        {
            var m = UnifiedBackupRegex.Match(fileName);
            if (m.Success && m.Groups["tag"].Success)
            {
                var tag = m.Groups["tag"].Value.Trim();
                if (!string.IsNullOrEmpty(tag))
                    return tag;
            }

            // 兼容：user1_20250909_151651_tag.dat / json
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

        public string GenerateBeforeRestoreBackupPath()
        {
            if (!HasDatFile) return string.Empty;
            return Path.Combine(DirectoryPath, $"{BaseName}.before_restore.bak");
        }

        public void RefreshLocalizedProperties()
        {
            foreach (var backup in BackupVersions)
            {
                backup.RefreshLocalizedProperties();
            }
            
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