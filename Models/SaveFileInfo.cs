using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HollowKnightSaveParser.Models
{
    public class SaveFileInfo
    {
        public int SlotNumber { get; set; }
        public string BaseName { get; set; } = string.Empty;
        public string DatFilePath { get; set; } = string.Empty;
        public string JsonFilePath { get; set; } = string.Empty;
        
        // 添加相关文件列表（备份文件、mod文件等）
        public List<string> RelatedFiles { get; set; } = new();
        
        public bool HasDatFile => !string.IsNullOrEmpty(DatFilePath) && File.Exists(DatFilePath);
        public bool HasJsonFile => !string.IsNullOrEmpty(JsonFilePath) && File.Exists(JsonFilePath);
        
        public bool CanBackup => HasDatFile;
        public bool CanRestore => HasBackupFiles;
        
        // 显示的文件名 - 显示实际的文件名
        public string DisplayFileName 
        { 
            get
            {
                if (HasDatFile && HasJsonFile)
                {
                    // 如果两个文件都存在，显示基础名称
                    return BaseName;
                }
                else if (HasDatFile)
                {
                    return Path.GetFileName(DatFilePath);
                }
                else if (HasJsonFile)
                {
                    return Path.GetFileName(JsonFilePath);
                }
                return BaseName;
            }
        }
        
        // 文件分类属性
        // 文件分类属性
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
            
                    // 标准存档文件
                    if (Regex.IsMatch(fileName, @"^user\d+\.(dat|json)$"))
                    {
                        categories["standard"].Add(file);
                    }
                    // Mod 数据文件
                    else if (fileName.Contains("modded"))
                    {
                        categories["mod"].Add(file);
                    }
                    // 备份文件 - 修复这里的逻辑
                    else if (fileName.Contains("bak") || 
                             fileName.Contains("backup") ||
                             Regex.IsMatch(fileName, @"user\d+\.\d{8}_\d{6}\.dat\.bak$") ||
                             Regex.IsMatch(fileName, @"user\d+_[\d\.]+\.(dat|json)$"))
                    {
                        categories["backup"].Add(file);
                    }
                    // 其他文件
                    else
                    {
                        categories["other"].Add(file);
                    }
                }

                return categories;
            }
        }
        
        public string FileStatusText
        {
            get
            {
                if (HasDatFile && HasJsonFile)
                    return "DAT + JSON";
                else if (HasDatFile)
                    return "DAT";
                else if (HasJsonFile)
                    return "JSON";
                else
                    return "错误";
            }
        }
        
        // 文件类型描述
        public string FileTypeDisplayText
        {
            get
            {
                if (HasDatFile && HasJsonFile)
                    return "存档文件 (双格式)";
                else if (HasDatFile)
                    return "存档文件 (二进制)";
                else if (HasJsonFile)
                    return "存档文件 (JSON)";
                else
                    return "未知格式";
            }
        }
        
        // 详细的文件信息
        public string DetailedFileInfo
        {
            get
            {
                var info = new List<string>();
                
                if (HasDatFile)
                {
                    var datInfo = new FileInfo(DatFilePath);
                    info.Add($"DAT: {FormatFileSize(datInfo.Length)}");
                }
                
                if (HasJsonFile)
                {
                    var jsonInfo = new FileInfo(JsonFilePath);
                    info.Add($"JSON: {FormatFileSize(jsonInfo.Length)}");
                }
                
                if (RelatedFiles.Count > 0)
                {
                    info.Add($"相关文件: {RelatedFiles.Count}个");
                }
                
                return string.Join(" | ", info);
            }
        }
        
        // 详细的文件类型描述
        public string DetailedFileTypeInfo
        {
            get
            {
                var categories = CategorizedRelatedFiles;
                var info = new List<string>();
                
                if (HasDatFile || HasJsonFile)
                {
                    info.Add($"存档: {(HasDatFile && HasJsonFile ? "DAT+JSON" : HasDatFile ? "DAT" : "JSON")}");
                }
                
                if (categories["mod"].Count > 0)
                {
                    var modFiles = string.Join(", ", categories["mod"]);
                    info.Add($"Mod数据: {modFiles}");
                }
                
                if (categories["backup"].Count > 0)
                {
                    info.Add($"备份文件: {categories["backup"].Count}个");
                }
                
                return string.Join(" | ", info);
            }
        }
        
        public long FileSize
        {
            get
            {
                // 优先返回 DAT 文件大小（原生格式），如果没有则返回 JSON 文件大小
                if (HasDatFile && File.Exists(DatFilePath))
                {
                    return new FileInfo(DatFilePath).Length;
                }
        
                if (HasJsonFile && File.Exists(JsonFilePath))
                {
                    return new FileInfo(JsonFilePath).Length;
                }
        
                return 0;
            }
        }
        
        public string FormattedFileSize => FormatFileSize(FileSize);
        
        // 提取为独立方法，可复用
        private static string FormatFileSize(long size)
        {
            if (size < 1024)
                return $"{size} B";
            else if (size < 1024 * 1024)
                return $"{size / 1024.0:F1} KB";
            else
                return $"{size / (1024.0 * 1024.0):F1} MB";
        }
        
        // 修改时间（取最新的文件）
        public DateTime LastModified
        {
            get
            {
                DateTime datTime = HasDatFile ? File.GetLastWriteTime(DatFilePath) : DateTime.MinValue;
                DateTime jsonTime = HasJsonFile ? File.GetLastWriteTime(JsonFilePath) : DateTime.MinValue;
                return datTime > jsonTime ? datTime : jsonTime;
            }
        }
        
        // 格式化的修改时间
        public string FormattedLastModified => LastModified == DateTime.MinValue 
            ? "未知" 
            : LastModified.ToString("yyyy-MM-dd HH:mm:ss");
        
        // 转换按钮状态
        public bool CanConvertToJson => HasDatFile;
        public bool CanConvertToDat => HasJsonFile;
        
        public string DatToJsonButtonText => HasJsonFile ? "更新JSON" : "转为JSON";
        public string JsonToDatButtonText => HasDatFile ? "更新DAT" : "转为DAT";
        
        // 工具提示信息
        public string ToolTipText
        {
            get
            {
                var tooltip = new List<string>
                {
                    $"槽位: {SlotNumber}",
                    $"状态: {FileStatusText}",
                    $"大小: {FormattedFileSize}",
                    $"修改: {FormattedLastModified}"
                };
                
                if (HasDatFile)
                    tooltip.Add($"DAT文件: {Path.GetFileName(DatFilePath)}");
                
                if (HasJsonFile)
                    tooltip.Add($"JSON文件: {Path.GetFileName(JsonFilePath)}");
                
                var categories = CategorizedRelatedFiles;
                
                if (categories["mod"].Count > 0)
                {
                    tooltip.Add($"Mod数据文件:");
                    foreach (var modFile in categories["mod"])
                    {
                        tooltip.Add($"  • {modFile}");
                    }
                }
                
                if (categories["backup"].Count > 0)
                {
                    tooltip.Add($"备份文件: {string.Join(", ", categories["backup"])}");
                }
                
                if (categories["other"].Count > 0)
                {
                    tooltip.Add($"其他文件: {string.Join(", ", categories["other"])}");
                }
                
                return string.Join("\n", tooltip);
            }
        }
        
        // 文件完整性检查
        public bool IsValid
        {
            get
            {
                try
                {
                    if (HasDatFile && !File.Exists(DatFilePath))
                        return false;
                    
                    if (HasJsonFile && !File.Exists(JsonFilePath))
                        return false;
                    
                    // 检查文件大小是否合理（存档文件不应该为空）
                    if (HasDatFile && new FileInfo(DatFilePath).Length == 0)
                        return false;
                    
                    if (HasJsonFile && new FileInfo(JsonFilePath).Length == 0)
                        return false;
                    
                    return HasDatFile || HasJsonFile;
                }
                catch
                {
                    return false;
                }
            }
        }
        
        // 获取主要文件路径（优先DAT）
        public string PrimaryFilePath
        {
            get
            {
                if (HasDatFile) return DatFilePath;
                if (HasJsonFile) return JsonFilePath;
                return string.Empty;
            }
        }
        
        // 获取文件夹路径
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
        
        // 是否有 Mod 数据
        public bool HasModData => CategorizedRelatedFiles["mod"].Count > 0;

        // Mod 数据文件路径
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
                return categories["backup"].Any(f => f.EndsWith(".dat.bak", StringComparison.OrdinalIgnoreCase));
            }
        }

// 获取最新的备份文件路径
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

// 生成备份文件路径
        public string GenerateBackupFilePath()
        {
            if (!HasDatFile) return string.Empty;
    
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.ChangeExtension(DatFilePath, $".{timestamp}.dat.bak");
        }
    }
}
