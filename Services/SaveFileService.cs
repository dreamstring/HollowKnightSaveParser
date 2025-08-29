// Services/SaveFileService.cs
using System;
using System.Collections.Generic;
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
                // 1. 读取并解析 DAT 文件
                string base64EncryptedData = ExtractBase64FromDatFile(datFilePath);
                
                // 2. AES 解密
                string jsonString = DecryptAES(base64EncryptedData);
                
                // 3. 格式化 JSON
                var jsonObject = JObject.Parse(jsonString);
                var formattedJson = jsonObject.ToString(Formatting.Indented);
                
                // 4. 保存文件
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
                // 1. 读取 JSON
                var jsonContent = await File.ReadAllTextAsync(jsonFilePath, Encoding.UTF8);
                var compactJson = JObject.Parse(jsonContent).ToString(Formatting.None);
                
                // 2. AES 加密
                string base64EncryptedData = EncryptAES(compactJson);
                
                // 3. 创建 DAT 文件
                outputPath ??= Path.ChangeExtension(jsonFilePath, ".dat");
                CreateDatFile(outputPath, base64EncryptedData);
                
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

        private string ExtractBase64FromDatFile(string filePath)
        {
            var fileBytes = File.ReadAllBytes(filePath);
            
            // 方法1: 直接搜索 Base64 模式
            var fileText = Encoding.UTF8.GetString(fileBytes);
            var base64Match = Regex.Match(fileText, @"[A-Za-z0-9+/]{100,}={0,2}");
            if (base64Match.Success)
            {
                var candidate = base64Match.Value;
                if (IsValidBase64(candidate))
                {
                    return candidate;
                }
            }
            
            // 方法2: 手动解析二进制数据
            return ParseBinaryData(fileBytes);
        }

        private string ParseBinaryData(byte[] data)
        {
            // 查找所有可能的字符串
            var candidates = new List<string>();
            
            for (int i = 0; i < data.Length - 4; i++)
            {
                // 查找字符串长度标记
                if (data[i] == 0x06 || data[i] == 0x12) // BinaryFormatter 字符串标记
                {
                    try
                    {
                        int length = ReadVariableLength(data, i + 1, out int bytesRead);
                        int startPos = i + 1 + bytesRead;
                        
                        if (length > 50 && length < 10000 && startPos + length <= data.Length)
                        {
                            var stringBytes = new byte[length];
                            Array.Copy(data, startPos, stringBytes, 0, length);
                            var candidate = Encoding.UTF8.GetString(stringBytes);
                            
                            if (IsValidBase64(candidate))
                            {
                                return candidate;
                            }
                        }
                    }
                    catch { continue; }
                }
            }
            
            // 方法3: 暴力搜索连续的可打印字符
            var currentString = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if ((b >= 32 && b <= 126) || b == 43 || b == 47 || b == 61) // 可打印字符 + Base64 字符
                {
                    currentString.Append((char)b);
                }
                else
                {
                    if (currentString.Length > 100)
                    {
                        var candidate = currentString.ToString();
                        if (IsValidBase64(candidate))
                        {
                            return candidate;
                        }
                    }
                    currentString.Clear();
                }
            }
            
            // 最后检查
            if (currentString.Length > 100)
            {
                var candidate = currentString.ToString();
                if (IsValidBase64(candidate))
                {
                    return candidate;
                }
            }
            
            throw new InvalidDataException("无法从DAT文件中提取Base64数据");
        }

        private int ReadVariableLength(byte[] data, int startIndex, out int bytesRead)
        {
            bytesRead = 0;
            int result = 0;
            int shift = 0;
            
            for (int i = startIndex; i < data.Length && bytesRead < 5; i++, bytesRead++)
            {
                byte b = data[i];
                result |= (b & 0x7F) << shift;
                shift += 7;
                
                if ((b & 0x80) == 0)
                {
                    bytesRead++;
                    break;
                }
            }
            
            return result;
        }

        private void CreateDatFile(string filePath, string base64Data)
        {
            using var stream = new FileStream(filePath, FileMode.Create);
            using var writer = new BinaryWriter(stream);
            
            // 简化的 BinaryFormatter 头部
            writer.Write(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
            
            // 写入字符串数据
            writer.Write((byte)0x06); // 字符串类型
            WriteVariableLength(writer, base64Data.Length);
            writer.Write(Encoding.UTF8.GetBytes(base64Data));
            
            // 结束标记
            writer.Write((byte)0x0B);
        }

        private void WriteVariableLength(BinaryWriter writer, int value)
        {
            while (value >= 0x80)
            {
                writer.Write((byte)(value | 0x80));
                value >>= 7;
            }
            writer.Write((byte)value);
        }

        private string DecryptAES(string base64Data)
        {
            var encryptedBytes = Convert.FromBase64String(base64Data);
            
            using var aes = Aes.Create();
            aes.Key = HollowKnightKey;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.PKCS7;
            
            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
            
            return Encoding.UTF8.GetString(decryptedBytes);
        }

        private string EncryptAES(string plainText)
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            
            using var aes = Aes.Create();
            aes.Key = HollowKnightKey;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.PKCS7;
            
            using var encryptor = aes.CreateEncryptor();
            var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            
            return Convert.ToBase64String(encryptedBytes);
        }

        private bool IsValidBase64(string input)
        {
            if (string.IsNullOrEmpty(input) || input.Length < 50)
                return false;
            
            // 检查 Base64 字符集
            if (!Regex.IsMatch(input, @"^[A-Za-z0-9+/]*={0,2}$"))
                return false;
            
            // 检查长度
            if (input.Length % 4 != 0)
                return false;
            
            try
            {
                var decoded = Convert.FromBase64String(input);
                return decoded.Length > 10; // 解密后应该有实际内容
            }
            catch
            {
                return false;
            }
        }
    }
}
