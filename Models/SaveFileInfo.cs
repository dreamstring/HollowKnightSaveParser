using System;
using System.IO;

namespace HollowKnightSaveParser.Models
{
    public class SaveFileInfo
    {
        public int SlotNumber { get; set; }
        public string BaseName { get; set; } = string.Empty;
        public string DatFilePath { get; set; } = string.Empty;
        public string JsonFilePath { get; set; } = string.Empty;
        
        public bool HasDatFile => !string.IsNullOrEmpty(DatFilePath) && File.Exists(DatFilePath);
        public bool HasJsonFile => !string.IsNullOrEmpty(JsonFilePath) && File.Exists(JsonFilePath);
        
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
        
        // 文件状态
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
        
        public string FormattedFileSize
        {
            get
            {
                var size = FileSize;
                if (size < 1024)
                    return $"{size} B";
                else if (size < 1024 * 1024)
                    return $"{size / 1024.0:F1} KB";
                else
                    return $"{size / (1024.0 * 1024.0):F1} MB";
            }
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
        
        // 转换按钮状态
        public bool CanConvertToJson => HasDatFile;
        public bool CanConvertToDat => HasJsonFile;
        
        public string DatToJsonButtonText => HasJsonFile ? "更新JSON" : "转为JSON";
        public string JsonToDatButtonText => HasDatFile ? "更新DAT" : "转为DAT";
    }
}
