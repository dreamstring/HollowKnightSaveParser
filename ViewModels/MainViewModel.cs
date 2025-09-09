using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using HollowKnightSaveParser.Models;
using HollowKnightSaveParser.Services;
using Wpf.Ui.Controls;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;

namespace HollowKnightSaveParser.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly SaveFileService _saveFileService = new();

        [ObservableProperty] private ObservableCollection<SaveFileInfo> _saveFiles = new();

        [ObservableProperty] private bool _isLoading;

        [ObservableProperty] private bool _isStatusVisible;

        [ObservableProperty] private string _statusMessage = string.Empty;

        [ObservableProperty] private string _statusDetail = string.Empty;

        [ObservableProperty] private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;

        [ObservableProperty] private int _selectedGameIndex = 0; // 0: 空洞骑士, 1: 丝之歌

        [ObservableProperty] private ObservableCollection<SteamUser> _steamUsers = new();

        [ObservableProperty] private SteamUser? _selectedSteamUser;

        // 添加操作历史跟踪
        [ObservableProperty] private string _lastOperation = string.Empty;

        [ObservableProperty] private DateTime _lastOperationTime = DateTime.MinValue;

        [ObservableProperty] private Dictionary<string, object> _lastOperationDetails = new();

        // Steam相关
        [ObservableProperty] private bool _isSilksongMode;

        [ObservableProperty] private ObservableCollection<string> _availableSteamIds = new();

        [ObservableProperty] private string? _selectedSteamId;

        public bool HasFiles => SaveFiles.Count > 0;
        public bool HasNoFiles => !IsLoading && SaveFiles.Count == 0;
        public string SaveDirectory { get; private set; } = string.Empty;
        public string FileCountText => $"找到 {SaveFiles.Count} 个存档槽位";

        // Steam ID 变化处理
        private void OnSteamIdChanged()
        {
            if (IsSilksongMode && !string.IsNullOrEmpty(SelectedSteamId))
            {
                var silksongBasePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "..", "LocalLow", "Team Cherry", "Hollow Knight Silksong");
                silksongBasePath = Path.GetFullPath(silksongBasePath);

                SaveDirectory = Path.Combine(silksongBasePath, SelectedSteamId);
                OnPropertyChanged(nameof(SaveDirectory));
                _ = LoadSaveFilesAsync();
            }
        }

        // 加载可用的 Steam ID
        private async Task LoadAvailableSteamIds()
        {
            try
            {
                var silksongBasePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "..", "LocalLow", "Team Cherry", "Hollow Knight Silksong");
                silksongBasePath = Path.GetFullPath(silksongBasePath);

                if (!Directory.Exists(silksongBasePath))
                {
                    AvailableSteamIds.Clear();
                    ShowStatus("提示", "未找到丝之歌存档目录", InfoBarSeverity.Warning);
                    return;
                }

                var steamIdDirs = Directory.GetDirectories(silksongBasePath)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrEmpty(name) && IsValidSteamId(name))
                    .OrderBy(id => id)
                    .ToList();

                AvailableSteamIds.Clear();
                foreach (var steamId in steamIdDirs)
                {
                    AvailableSteamIds.Add(steamId);
                }

                // 默认选择第一个
                if (AvailableSteamIds.Count > 0 && string.IsNullOrEmpty(SelectedSteamId))
                {
                    SelectedSteamId = AvailableSteamIds[0];
                }
                else if (AvailableSteamIds.Count == 0)
                {
                    ShowStatus("提示", "未找到 Steam 用户目录", InfoBarSeverity.Warning);
                }
            }
            catch (Exception ex)
            {
                ShowStatus("错误", $"加载 Steam ID 失败: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        // 验证是否为有效的 Steam ID
        private static bool IsValidSteamId(string steamId)
        {
            return !string.IsNullOrEmpty(steamId) &&
                   steamId.All(char.IsDigit) &&
                   steamId.Length >= 8; // Steam ID 通常比较长
        }

        // SelectGame 命令方法
        [RelayCommand]
        private async Task SelectGame(string gameIndex)
        {
            var index = int.Parse(gameIndex);
            SelectedGameIndex = index;
            IsSilksongMode = index == 1;
    
            if (IsSilksongMode)
            {
                await LoadAvailableSteamIds();
            }
            else
            {
                AvailableSteamIds.Clear();
                SelectedSteamId = null;
                InitializeSaveDirectory();
                await LoadSaveFilesAsync();
            }
        }
        
        public void SetLastOperation(string operation, Dictionary<string, object> details)
        {
            LastOperation = operation;
            LastOperationTime = DateTime.Now;
            LastOperationDetails = details ?? new Dictionary<string, object>();
        }

        public MainViewModel()
        {
            // 监听集合变化
            SaveFiles.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasNoFiles));
                OnPropertyChanged(nameof(HasFiles));
                OnPropertyChanged(nameof(FileCountText));
            };


            // 监听游戏切换
            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SelectedGameIndex))
                {
                    OnGameChanged();
                }
                else if (e.PropertyName == nameof(SelectedSteamUser))
                {
                    OnSteamUserChanged();
                }
                else if (e.PropertyName == nameof(SelectedSteamId))
                {
                    OnSteamIdChanged();
                }
            };

            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            await RefreshSteamUsersAsync();
            await LoadSaveFilesAsync();
        }

        partial void OnIsLoadingChanged(bool value)
        {
            OnPropertyChanged(nameof(HasNoFiles));
            OnPropertyChanged(nameof(HasFiles));
        }

        partial void OnSaveFilesChanged(ObservableCollection<SaveFileInfo> value)
        {
            OnPropertyChanged(nameof(HasNoFiles));
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(FileCountText));
        }


        private void OnGameChanged()
        {
            IsSilksongMode = SelectedGameIndex == 1;

            if (SelectedGameIndex == 1) // 丝之歌
            {
                _ = RefreshSteamUsersAsync();
                _ = LoadAvailableSteamIds();
            }
            else // 空洞骑士
            {
                AvailableSteamIds.Clear();
                SelectedSteamId = null;
                InitializeSaveDirectory();
                _ = LoadSaveFilesAsync();
            }
        }

        private void OnSteamUserChanged()
        {
            if (SelectedGameIndex == 1 && SelectedSteamUser != null) // 丝之歌
            {
                SaveDirectory = SelectedSteamUser.FolderPath;
                OnPropertyChanged(nameof(SaveDirectory));
                _ = LoadSaveFilesAsync();
            }
        }

        private void InitializeSaveDirectory()
        {
            if (SelectedGameIndex == 0) // 空洞骑士
            {
                var localLowPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "..", "LocalLow", "Team Cherry", "Hollow Knight");
                localLowPath = Path.GetFullPath(localLowPath);

                SaveDirectory = Directory.Exists(localLowPath) ? localLowPath : string.Empty;
            }
            else if (SelectedGameIndex == 1 && !string.IsNullOrEmpty(SelectedSteamId)) // 丝之歌
            {
                var silksongBasePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "..", "LocalLow", "Team Cherry", "Hollow Knight Silksong");
                silksongBasePath = Path.GetFullPath(silksongBasePath);

                SaveDirectory = Path.Combine(silksongBasePath, SelectedSteamId);
            }

            OnPropertyChanged(nameof(SaveDirectory));
        }


        [RelayCommand]
        private async Task RefreshSteamUsersAsync()
        {
            if (SelectedGameIndex != 1) return; // 只在丝之歌模式下执行

            try
            {
                SteamUsers.Clear();

                var silksongBasePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "..", "LocalLow", "Team Cherry", "Hollow Knight Silksong");
                silksongBasePath = Path.GetFullPath(silksongBasePath);

                if (!Directory.Exists(silksongBasePath))
                {
                    ShowStatus("提示", "未找到丝之歌存档目录", InfoBarSeverity.Warning);
                    return;
                }

                var userDirectories = Directory.GetDirectories(silksongBasePath)
                    .Where(dir => Regex.IsMatch(Path.GetFileName(dir), @"^\d+$"))
                    .ToArray();

                foreach (var userDir in userDirectories)
                {
                    var userId = Path.GetFileName(userDir);
                    var steamUser = new SteamUser
                    {
                        UserId = userId,
                        DisplayName = $"Steam 用户 {userId}",
                        FolderPath = userDir
                    };
                    SteamUsers.Add(steamUser);
                }

                if (SteamUsers.Count > 0)
                {
                    SelectedSteamUser = SteamUsers.First();
                    ShowStatus("成功", $"找到 {SteamUsers.Count} 个 Steam 用户", InfoBarSeverity.Success);
                }
                else
                {
                    ShowStatus("提示", "未找到 Steam 用户目录", InfoBarSeverity.Warning);
                }
            }
            catch (Exception ex)
            {
                ShowStatus("错误", $"刷新 Steam 用户时出错: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private (string Title, string Detail) GetBackupCompleteMessage(string baseDetail)
        {
            if (LastOperationDetails.TryGetValue("sourceFile", out var sourceObj) &&
                LastOperationDetails.TryGetValue("backupFile", out var backupObj))
            {
                var sourceFile = sourceObj.ToString();
                var backupFile = Path.GetFileName(backupObj.ToString());

                return ("备份完成", $"{baseDetail}，已将 {sourceFile} 备份为 {backupFile}");
            }

            return ("备份完成", $"{baseDetail}，备份操作已生效");
        }

        private (string Title, string Detail) GetRestoreCompleteMessage(string baseDetail)
        {
            if (LastOperationDetails.TryGetValue("backupFile", out var backupObj) &&
                LastOperationDetails.TryGetValue("targetFile", out var targetObj))
            {
                var backupFile = Path.GetFileName(backupObj.ToString());
                var targetFile = targetObj.ToString();

                return ("恢复完成", $"{baseDetail}，已从 {backupFile} 恢复到 {targetFile}");
            }

            return ("恢复完成", $"{baseDetail}，存档已从备份恢复");
        }

        [RelayCommand]
        private async Task LoadSaveFilesAsync()
        {
            IsLoading = true;
            SaveFiles.Clear();

            try
            {
                if (SelectedGameIndex == 0) // 空洞骑士
                {
                    InitializeSaveDirectory();
                }
                else if (SelectedGameIndex == 1 && SelectedSteamUser == null) // 丝之歌但未选择用户
                {
                    ShowStatus("提示", "请先选择 Steam 用户", InfoBarSeverity.Warning);
                    return;
                }

                if (!Directory.Exists(SaveDirectory))
                {
                    ShowStatus("错误", "存档目录不存在", InfoBarSeverity.Error);
                    return;
                }

                await Task.Run(() =>
                {
                    var saveFileGroups = GroupSaveFiles();

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        SaveFiles.Clear();
                        foreach (var group in saveFileGroups.OrderBy(g => g.SlotNumber))
                        {
                            SaveFiles.Add(group);
                        }
                    });
                });

                if (SaveFiles.Count > 0)
                {
                    // 根据最近操作显示不同信息
                    var statusMessage = GetLoadCompleteMessage();
                    ShowStatus(statusMessage.Title, statusMessage.Detail, InfoBarSeverity.Success);
                }
                else
                {
                    ShowStatus("提示", "未找到有效的存档文件", InfoBarSeverity.Warning);
                }
            }
            catch (Exception ex)
            {
                ShowStatus("加载失败", ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private (string Title, string Detail) GetLoadCompleteMessage()
        {
            var fileCount = SaveFiles.Count;
            var baseDetail = $"找到 {fileCount} 个存档槽位";

            // 检查是否有最近操作（5秒内） - 使用属性而不是字段
            if (!string.IsNullOrEmpty(LastOperation) &&
                DateTime.Now - LastOperationTime < TimeSpan.FromSeconds(5))
            {
                return LastOperation switch
                {
                    "backup" => GetBackupCompleteMessage(baseDetail),
                    "restore" => GetRestoreCompleteMessage(baseDetail),
                    "convert_to_json" => GetConvertCompleteMessage("JSON", baseDetail),
                    "convert_to_dat" => GetConvertCompleteMessage("DAT", baseDetail),
                    "delete_dat" => GetDeleteCompleteMessage("DAT", baseDetail),
                    "delete_json" => GetDeleteCompleteMessage("JSON", baseDetail),
                    _ => ("刷新完成", baseDetail)
                };
            }

            // 分析存档状态
            var totalBackups = SaveFiles.Sum(s => s.BackupCount);
            var jsonCount = SaveFiles.Count(s => s.HasJsonFile);
            var datCount = SaveFiles.Count(s => s.HasDatFile);
            var modCount = SaveFiles.Count(s => s.HasModData);

            var details = new List<string> { baseDetail };

            if (totalBackups > 0)
            {
                details.Add($"检测到 {totalBackups} 个备份文件");
            }

            if (modCount > 0)
            {
                details.Add($"{modCount} 个存档含Mod数据");
            }

            if (jsonCount > 0 && datCount > 0)
            {
                details.Add($"包含 {datCount} 个DAT和 {jsonCount} 个JSON");
            }
            else if (jsonCount > 0)
            {
                details.Add($"{jsonCount} 个为JSON格式");
            }
            else if (datCount > 0)
            {
                details.Add($"{datCount} 个为DAT格式");
            }

            return ("加载完成", string.Join("，", details));
        }

        private (string Title, string Detail) GetConvertCompleteMessage(string targetFormat, string baseDetail)
        {
            if (LastOperationDetails.TryGetValue("sourceFile", out var sourceObj) &&
                LastOperationDetails.TryGetValue("targetFile", out var targetObj))
            {
                var sourceFile = sourceObj.ToString();
                var targetFile = targetObj.ToString();

                return ("转换完成", $"{baseDetail}，已将 {sourceFile} 转换为 {targetFile}");
            }

            return ("转换完成", $"{baseDetail}，{(targetFormat == "JSON" ? "DAT已转换为JSON" : "JSON已转换为DAT")}");
        }

        private (string Title, string Detail) GetDeleteCompleteMessage(string fileType, string baseDetail)
        {
            if (LastOperationDetails.TryGetValue("deletedFile", out var deletedObj))
            {
                var deletedFile = deletedObj.ToString();
                return ("删除完成", $"{baseDetail}，已删除 {deletedFile}");
            }

            return ("删除完成", $"{baseDetail}，{fileType}文件已删除");
        }

        [RelayCommand]
        private async Task BackupSaveFileAsync(SaveFileInfo? saveFile)
        {
            if (saveFile == null || !saveFile.CanBackup) return;

            try
            {
                var sourceFile = Path.GetFileName(saveFile.DatFilePath);
                var backupPath = saveFile.GenerateBackupFilePath();
                var backupFile = Path.GetFileName(backupPath);

                // 执行备份操作
                File.Copy(saveFile.DatFilePath, backupPath);

                // 记录具体操作信息
                SetLastOperation("backup", new Dictionary<string, object>
                {
                    ["sourceFile"] = sourceFile,
                    ["backupFile"] = backupPath
                });

                await LoadSaveFilesAsync();
            }
            catch (Exception ex)
            {
                ShowStatus("备份失败", $"备份 {saveFile.DisplayFileName} 时出错: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        [RelayCommand]
        private async Task RestoreSaveFileAsync(SaveFileInfo? saveFile)
        {
            if (saveFile == null || !saveFile.CanRestore) return;

            try
            {
                var backupPath = saveFile.LatestBackupFilePath;
                if (string.IsNullOrEmpty(backupPath))
                {
                    ShowStatus("恢复失败", "未找到有效的备份文件", InfoBarSeverity.Error);
                    return;
                }

                // 弹出确认对话框
                var result = await ShowConfirmationDialogAsync(
                    "确认恢复",
                    $"确定要从备份恢复 {saveFile.DisplayFileName} 吗？\n\n" +
                    $"备份文件: {Path.GetFileName(backupPath)}\n" +
                    $"当前存档将被覆盖，此操作不可撤销！");

                if (!result) return;

                ShowStatus("恢复中", $"正在恢复 {saveFile.DisplayFileName}...", InfoBarSeverity.Informational);

                // 确定目标文件路径
                string targetPath;
                if (saveFile.HasDatFile && !string.IsNullOrEmpty(saveFile.DatFilePath))
                {
                    // 如果已有 DAT 文件，先备份当前文件
                    var currentBackupPath = saveFile.DatFilePath + ".before_restore.bak";
                    File.Copy(saveFile.DatFilePath, currentBackupPath, true);
                    targetPath = saveFile.DatFilePath;
                }
                else
                {
                    // 如果没有 DAT 文件，构造目标路径
                    targetPath = Path.Combine(saveFile.DirectoryPath, $"{saveFile.BaseName}.dat");
                }

                // 从备份恢复
                File.Copy(backupPath, targetPath, true);

                // 记录具体操作信息 - 统一在这里设置
                SetLastOperation("restore", new Dictionary<string, object>
                {
                    ["backupFile"] = backupPath,
                    ["targetFile"] = Path.GetFileName(targetPath)
                });

                await LoadSaveFilesAsync();
            }
            catch (Exception ex)
            {
                ShowStatus("恢复失败", $"恢复 {saveFile.DisplayFileName} 时出错: {ex.Message}", InfoBarSeverity.Error);
            }
        }


        [RelayCommand]
        private async Task DeleteDatFileAsync(SaveFileInfo? saveFile)
        {
            if (saveFile == null || !saveFile.HasDatFile) return;

            try
            {
                var result = await ShowConfirmationDialogAsync(
                    "确认删除",
                    $"确定要删除 {saveFile.DisplayFileName} 的 DAT 文件吗？\n\n" +
                    $"文件路径: {saveFile.DatFilePath}\n" +
                    $"此操作不可撤销！");

                if (!result) return;

                var deletedFile = Path.GetFileName(saveFile.DatFilePath!);
                File.Delete(saveFile.DatFilePath!);

                // 记录具体操作信息
                SetLastOperation("delete_dat", new Dictionary<string, object>
                {
                    ["deletedFile"] = deletedFile
                });

                await LoadSaveFilesAsync(); // 刷新列表
            }
            catch (Exception ex)
            {
                ShowStatus("删除失败", $"删除 DAT 文件时出错: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        [RelayCommand]
        private async Task DeleteJsonFileAsync(SaveFileInfo? saveFile)
        {
            if (saveFile == null || !saveFile.HasJsonFile) return;

            try
            {
                var result = await ShowConfirmationDialogAsync(
                    "确认删除",
                    $"确定要删除 {saveFile.DisplayFileName} 的 JSON 文件吗？\n\n" +
                    $"文件路径: {saveFile.JsonFilePath}\n" +
                    $"此操作不可撤销！");

                if (!result) return;

                var deletedFile = Path.GetFileName(saveFile.JsonFilePath!);
                File.Delete(saveFile.JsonFilePath!);

                // 记录具体操作信息
                SetLastOperation("delete_json", new Dictionary<string, object>
                {
                    ["deletedFile"] = deletedFile
                });

                await LoadSaveFilesAsync(); // 刷新列表
            }
            catch (Exception ex)
            {
                ShowStatus("删除失败", $"删除 JSON 文件时出错: {ex.Message}", InfoBarSeverity.Error);
            }
        }


        // 确认对话框方法 - 简化版本
        private async Task<bool> ShowConfirmationDialogAsync(string title, string message)
        {
            var dialog = new Wpf.Ui.Controls.MessageBox
            {
                Title = title,
                Content = message,
                PrimaryButtonText = "确认",
                CloseButtonText = "取消", // 将关闭按钮改名为"取消"
                // 不设置 SecondaryButtonText，这样就只有两个按钮
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var result = await dialog.ShowDialogAsync();
            return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
        }

        private SaveFileInfo[] GroupSaveFiles()
        {
            if (!Directory.Exists(SaveDirectory))
                return Array.Empty<SaveFileInfo>();

            var allFiles = Directory.GetFiles(SaveDirectory, "user*", SearchOption.TopDirectoryOnly);
            var saveFileGroups = new Dictionary<int, SaveFileInfo>();

            foreach (var filePath in allFiles)
            {
                var fileName = Path.GetFileName(filePath);

                // 扩展的匹配模式
                var patterns = new[]
                {
                    (@"^user(\d+)\.dat$", "dat", "standard"),
                    (@"^user(\d+)\.json$", "json", "standard"),
                    (@"^user(\d+)_[\d\.]+\.dat$", "dat", "backup"),
                    (@"^user(\d+)\.\d{8}_\d{6}\.dat\.bak$", "dat", "backup"),
                    (@"^user(\d+)\.dat\.bak\d*$", "dat", "backup"),
                    (@"^user(\d+)\.before_restore\.bak$", "dat", "backup"),
                    (@"^user(\d+)\.modded\.json$", "json", "mod"),
                    (@"^user(\d+).*modded.*$", "other", "mod")
                };

                foreach (var (pattern, fileType, category) in patterns)
                {
                    var match = Regex.Match(fileName, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var slotNumber = int.Parse(match.Groups[1].Value);

                        if (!saveFileGroups.ContainsKey(slotNumber))
                        {
                            saveFileGroups[slotNumber] = new SaveFileInfo
                            {
                                SlotNumber = slotNumber,
                                BaseName = $"user{slotNumber}"
                            };
                        }

                        var saveInfo = saveFileGroups[slotNumber];

                        // 设置标准文件路径
                        if (category == "standard")
                        {
                            if (fileType == "dat")
                                saveInfo.DatFilePath = filePath;
                            else if (fileType == "json")
                                saveInfo.JsonFilePath = filePath;
                        }

                        // 记录所有相关文件
                        saveInfo.RelatedFiles.Add(fileName);
                        break;
                    }
                }
            }

            return saveFileGroups.Values.OrderBy(s => s.SlotNumber).ToArray();
        }

        [RelayCommand]
        private async Task ConvertDatToJsonAsync(SaveFileInfo? saveFile)
        {
            if (saveFile == null || !saveFile.HasDatFile) return;

            try
            {
                ShowStatus("转换中", $"正在将 {saveFile.DisplayFileName} 转换为 JSON...", InfoBarSeverity.Informational);

                var result = await _saveFileService.ConvertDatToJsonAsync(saveFile.DatFilePath!);

                if (result.Success)
                {
                    // 记录具体操作信息
                    var sourceFile = Path.GetFileName(saveFile.DatFilePath!);
                    var targetFile = Path.ChangeExtension(sourceFile, ".json");

                    SetLastOperation("convert_to_json", new Dictionary<string, object>
                    {
                        ["sourceFile"] = sourceFile,
                        ["targetFile"] = targetFile
                    });

                    await LoadSaveFilesAsync(); // 刷新列表
                }
                else
                {
                    ShowStatus("转换失败", result.Message, InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                ShowStatus("转换失败", $"转换 {saveFile.DisplayFileName} 时出错: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        [RelayCommand]
        private async Task ConvertJsonToDatAsync(SaveFileInfo? saveFile)
        {
            if (saveFile == null || !saveFile.HasJsonFile) return;

            try
            {
                ShowStatus("转换中", $"正在将 {saveFile.DisplayFileName} 转换为 DAT...", InfoBarSeverity.Informational);

                var result = await _saveFileService.ConvertJsonToDatAsync(saveFile.JsonFilePath!);

                if (result.Success)
                {
                    // 记录具体操作信息
                    var sourceFile = Path.GetFileName(saveFile.JsonFilePath!);
                    var targetFile = Path.ChangeExtension(sourceFile, ".dat");

                    SetLastOperation("convert_to_dat", new Dictionary<string, object>
                    {
                        ["sourceFile"] = sourceFile,
                        ["targetFile"] = targetFile
                    });

                    await LoadSaveFilesAsync(); // 刷新列表
                }
                else
                {
                    ShowStatus("转换失败", result.Message, InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                ShowStatus("转换失败", $"转换 {saveFile.DisplayFileName} 时出错: {ex.Message}", InfoBarSeverity.Error);
            }
        }


        [RelayCommand]
        private void OpenSaveDirectory()
        {
            try
            {
                if (Directory.Exists(SaveDirectory))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = SaveDirectory,
                        UseShellExecute = true
                    });
                }
                else
                {
                    ShowStatus("错误", "存档目录不存在", InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                ShowStatus("打开失败", ex.Message, InfoBarSeverity.Error);
            }
        }

        private void ShowStatus(string message, string detail, InfoBarSeverity severity)
        {
            StatusMessage = message;
            StatusDetail = detail;
            StatusSeverity = severity;
            IsStatusVisible = true;

            // 3秒后自动隐藏成功和信息提示
            if (severity == InfoBarSeverity.Success || severity == InfoBarSeverity.Informational)
            {
                Task.Delay(3000).ContinueWith(_ => { App.Current.Dispatcher.Invoke(() => IsStatusVisible = false); });
            }
        }
    }
}