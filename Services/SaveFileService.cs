using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HollowKnightSaveParser.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HollowKnightSaveParser.Services
{
    public class SaveFileService
    {
        private static readonly byte[] HollowKnightKey = Encoding.UTF8.GetBytes("UKu52ePUBwetZ9wNX88o54dnfKRu0T1l");

        public async Task<ConversionResult> ConvertDatToJsonAsync(string datFilePath, string? outputPath = null)
        {
            try
            {
                string base64EncryptedData = ExtractBase64FromDatFile(datFilePath);
                string jsonString = DecryptData(base64EncryptedData);
                var jsonObject = JObject.Parse(jsonString);
                var formattedJson = jsonObject.ToString(Formatting.Indented);
                
                outputPath ??= Path.ChangeExtension(datFilePath, ".json");
                await File.WriteAllTextAsync(outputPath, formattedJson, Encoding.UTF8);
                
                return new ConversionResult
                {
                    Success = true,
                    Message = "DAT文件已成功转换为JSON格式",
                    OutputPath = outputPath
                };
            }
            catch (Exception ex)
            {
                return new ConversionResult
                {
                    Success = false,
                    Message = $"转换失败: {ex.Message}"
                };
            }
        }

        public async Task<ConversionResult> ConvertJsonToDatAsync(string jsonFilePath, string? outputPath = null)
        {
            try
            {
                var jsonContent = await File.ReadAllTextAsync(jsonFilePath, Encoding.UTF8);
                var compactJson = JObject.Parse(jsonContent).ToString(Formatting.None);
                
                string base64EncryptedData = EncryptData(compactJson);
                
                outputPath ??= Path.ChangeExtension(jsonFilePath, ".dat");
                
                // 关键：必须要有原始 DAT 文件作为模板
                string? templatePath = FindTemplateDatFile(jsonFilePath);
                if (templatePath == null)
                {
                    return new ConversionResult
                    {
                        Success = false,
                        Message = "错误：必须要有原始的 .dat 文件作为模板！\n" +
                                "请将要修改的原始 .dat 文件放在同一目录下，或者重命名为与 JSON 文件相同的名称。"
                    };
                }

                // 使用原始文件作为完美模板
                await CreateDatFromExactTemplate(templatePath, outputPath, base64EncryptedData);
                
                return new ConversionResult
                {
                    Success = true,
                    Message = "JSON文件已成功转换为DAT格式",
                    OutputPath = outputPath
                };
            }
            catch (Exception ex)
            {
                return new ConversionResult
                {
                    Success = false,
                    Message = $"转换失败: {ex.Message}"
                };
            }
        }

        // 加密解密方法 - 使用与你原始代码完全相同的实现
        private string EncryptData(string toEncrypt)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(toEncrypt);
            
            using var aes = Aes.Create();
            aes.Key = HollowKnightKey;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.PKCS7;
            aes.BlockSize = 128; // 确保与 RijndaelManaged 一致
            
            using var encryptor = aes.CreateEncryptor();
            byte[] encrypted = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);
            return Convert.ToBase64String(encrypted, 0, encrypted.Length);
        }

        private string DecryptData(string toDecrypt)
        {
            byte[] array = Convert.FromBase64String(toDecrypt);
            
            using var aes = Aes.Create();
            aes.Key = HollowKnightKey;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.PKCS7;
            aes.BlockSize = 128;
            
            using var decryptor = aes.CreateDecryptor();
            byte[] decrypted = decryptor.TransformFinalBlock(array, 0, array.Length);
            return Encoding.UTF8.GetString(decrypted);
        }

        // 查找模板文件 - 更智能的查找逻辑
        private string? FindTemplateDatFile(string jsonFilePath)
        {
            string directory = Path.GetDirectoryName(jsonFilePath) ?? "";
            string jsonFileName = Path.GetFileNameWithoutExtension(jsonFilePath);
            
            // 1. 优先查找同名的 DAT 文件
            string sameName = Path.Combine(directory, jsonFileName + ".dat");
            if (File.Exists(sameName))
            {
                return sameName;
            }
            
            // 2. 查找移除后缀的原始文件
            string[] suffixes = { "_backup", "_modified", "_edited", "_copy", "_new" };
            foreach (string suffix in suffixes)
            {
                if (jsonFileName.EndsWith(suffix))
                {
                    string originalName = jsonFileName.Substring(0, jsonFileName.Length - suffix.Length);
                    string originalPath = Path.Combine(directory, originalName + ".dat");
                    if (File.Exists(originalPath))
                    {
                        return originalPath;
                    }
                }
            }
            
            // 3. 查找同目录下任何有效的 DAT 文件
            var datFiles = Directory.GetFiles(directory, "*.dat");
            foreach (var datFile in datFiles)
            {
                try
                {
                    // 测试是否能成功解析
                    ExtractBase64FromDatFile(datFile);
                    return datFile;
                }
                catch
                {
                    continue;
                }
            }
            
            return null;
        }

        // 精确模板复制 - 这是关键方法
        private async Task CreateDatFromExactTemplate(string templatePath, string outputPath, string newBase64Data)
        {
            // 读取模板文件的所有字节
            byte[] templateBytes = await File.ReadAllBytesAsync(templatePath);
            
            // 提取模板中的旧 Base64 数据
            string oldBase64Data = ExtractBase64FromDatFile(templatePath);
            
            // 在字节级别进行精确替换
            byte[] newFileBytes = ReplaceBase64InBinaryData(templateBytes, oldBase64Data, newBase64Data);
            
            // 写入新文件
            await File.WriteAllBytesAsync(outputPath, newFileBytes);
            
            // 验证生成的文件
            try
            {
                string testBase64 = ExtractBase64FromDatFile(outputPath);
                string testJson = DecryptData(testBase64);
                JObject.Parse(testJson); // 验证 JSON 是否有效
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"生成的 DAT 文件验证失败: {ex.Message}");
            }
        }

        // 二进制数据替换 - 最关键的方法
        private byte[] ReplaceBase64InBinaryData(byte[] originalBytes, string oldBase64, string newBase64)
        {
            byte[] oldBase64Bytes = Encoding.UTF8.GetBytes(oldBase64);
            byte[] newBase64Bytes = Encoding.UTF8.GetBytes(newBase64);
            
            // 查找旧数据的确切位置
            int position = FindExactByteSequence(originalBytes, oldBase64Bytes);
            if (position == -1)
            {
                throw new InvalidOperationException("无法在模板文件中找到 Base64 数据的确切位置");
            }
            
            Console.WriteLine($"找到 Base64 数据位置: {position}");
            Console.WriteLine($"旧数据长度: {oldBase64Bytes.Length}, 新数据长度: {newBase64Bytes.Length}");
            
            // 创建新的字节数组
            int newLength = originalBytes.Length - oldBase64Bytes.Length + newBase64Bytes.Length;
            byte[] result = new byte[newLength];
            
            // 复制数据前的部分
            Array.Copy(originalBytes, 0, result, 0, position);
            
            // 插入新的 Base64 数据
            Array.Copy(newBase64Bytes, 0, result, position, newBase64Bytes.Length);
            
            // 复制数据后的部分
            int remainingStart = position + oldBase64Bytes.Length;
            int remainingLength = originalBytes.Length - remainingStart;
            if (remainingLength > 0)
            {
                Array.Copy(originalBytes, remainingStart, result, position + newBase64Bytes.Length, remainingLength);
            }
            
            // 如果长度发生变化，需要更新 BinaryFormatter 中的长度字段
            if (oldBase64Bytes.Length != newBase64Bytes.Length)
            {
                UpdateBinaryFormatterLength(result, position, oldBase64.Length, newBase64.Length);
            }
            
            return result;
        }

        // 查找精确的字节序列
        private int FindExactByteSequence(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return i;
                }
            }
            return -1;
        }

        // 更新 BinaryFormatter 中的长度字段
        private void UpdateBinaryFormatterLength(byte[] data, int dataPosition, int oldLength, int newLength)
        {
            // 在数据位置之前查找长度编码
            for (int i = Math.Max(0, dataPosition - 20); i < dataPosition; i++)
            {
                // 查找可能的长度编码位置
                if (TryUpdateLengthAt(data, i, oldLength, newLength))
                {
                    Console.WriteLine($"在位置 {i} 更新了长度字段");
                    break;
                }
            }
        }

        private bool TryUpdateLengthAt(byte[] data, int position, int oldLength, int newLength)
        {
            try
            {
                // 尝试读取变长整数
                int readLength = ReadVariableInt(data, position, out int bytesUsed);
                if (readLength == oldLength && bytesUsed > 0)
                {
                    // 找到了匹配的长度，更新它
                    WriteVariableInt(data, position, newLength, bytesUsed);
                    return true;
                }
            }
            catch
            {
                // 忽略错误，继续查找
            }
            return false;
        }

        private int ReadVariableInt(byte[] data, int position, out int bytesUsed)
        {
            bytesUsed = 0;
            int result = 0;
            int shift = 0;
            
            while (position + bytesUsed < data.Length && bytesUsed < 5)
            {
                byte b = data[position + bytesUsed];
                result |= (b & 0x7F) << shift;
                bytesUsed++;
                shift += 7;
                
                if ((b & 0x80) == 0)
                {
                    return result;
                }
            }
            
            throw new InvalidDataException("无效的变长整数");
        }

        private void WriteVariableInt(byte[] data, int position, int value, int maxBytes)
        {
            int bytesWritten = 0;
            while (value >= 0x80 && bytesWritten < maxBytes - 1)
            {
                data[position + bytesWritten] = (byte)(value | 0x80);
                value >>= 7;
                bytesWritten++;
            }
            
            if (bytesWritten < maxBytes)
            {
                data[position + bytesWritten] = (byte)value;
            }
        }

        // Base64 提取方法
        private string ExtractBase64FromDatFile(string filePath)
        {
            byte[] fileBytes = File.ReadAllBytes(filePath);
            
            // 方法1: 正则表达式查找
            string fileText = Encoding.UTF8.GetString(fileBytes);
            var matches = Regex.Matches(fileText, @"[A-Za-z0-9+/]{100,}={0,2}");
            
            foreach (Match match in matches)
            {
                if (IsValidBase64(match.Value))
                {
                    return match.Value;
                }
            }
            
            // 方法2: 字节扫描
            return ScanForBase64InBytes(fileBytes);
        }

        private string ScanForBase64InBytes(byte[] data)
        {
            var sb = new StringBuilder();
            
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                
                // Base64 字符范围
                if ((b >= 65 && b <= 90) ||   // A-Z
                    (b >= 97 && b <= 122) ||  // a-z
                    (b >= 48 && b <= 57) ||   // 0-9
                    b == 43 || b == 47 || b == 61) // +/=
                {
                    sb.Append((char)b);
                }
                else
                {
                    if (sb.Length > 100 && IsValidBase64(sb.ToString()))
                    {
                        return sb.ToString();
                    }
                    sb.Clear();
                }
            }
            
            // 检查最后的字符串
            if (sb.Length > 100 && IsValidBase64(sb.ToString()))
            {
                return sb.ToString();
            }
            
            throw new InvalidDataException("无法从 DAT 文件中提取有效的 Base64 数据");
        }

        private bool IsValidBase64(string input)
        {
            if (string.IsNullOrEmpty(input) || input.Length < 100 || input.Length % 4 != 0)
                return false;
            
            try
            {
                byte[] decoded = Convert.FromBase64String(input);
                return decoded.Length > 50; // 确保有足够的数据
            }
            catch
            {
                return false;
            }
        }
    }
}
