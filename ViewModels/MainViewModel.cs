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

        public ICommand SelectGameCommand { get; }

        public bool HasNoFiles => !IsLoading && SaveFiles.Count == 0;
        public string SaveDirectory { get; private set; } = string.Empty;
        public string FileCountText => $"找到 {SaveFiles.Count} 个存档槽位";

        public MainViewModel()
        {
            // 监听集合变化
            SaveFiles.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasNoFiles));
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
            };

            SelectGameCommand = new RelayCommand<string>(index => { SelectedGameIndex = int.Parse(index); });

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
        }

        partial void OnSaveFilesChanged(ObservableCollection<SaveFileInfo> value)
        {
            OnPropertyChanged(nameof(HasNoFiles));
            OnPropertyChanged(nameof(FileCountText));
        }

        private void OnGameChanged()
        {
            if (SelectedGameIndex == 1) // 丝之歌
            {
                _ = RefreshSteamUsersAsync();
            }
            else // 空洞骑士
            {
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
                    ShowStatus("加载完成", $"找到 {SaveFiles.Count} 个存档槽位", InfoBarSeverity.Success);
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

        // 在 MainViewModel.cs 中添加以下命令

        [RelayCommand]
        private async Task BackupSaveFileAsync(SaveFileInfo? saveFile)
        {
            if (saveFile == null || !saveFile.CanBackup) return;

            try
            {
                ShowStatus("备份中", $"正在备份 {saveFile.DisplayFileName}...", InfoBarSeverity.Informational);

                var backupPath = saveFile.GenerateBackupFilePath();
                File.Copy(saveFile.DatFilePath!, backupPath, true);

                ShowStatus("备份成功", $"已备份到: {Path.GetFileName(backupPath)}", InfoBarSeverity.Success);
                await LoadSaveFilesAsync(); // 刷新列表以显示新的备份文件
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

                ShowStatus("恢复成功", $"已从备份恢复 {saveFile.DisplayFileName}", InfoBarSeverity.Success);
                await LoadSaveFilesAsync(); // 刷新列表
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

                File.Delete(saveFile.DatFilePath!);
                ShowStatus("删除成功", $"已删除 {saveFile.DisplayFileName} 的 DAT 文件", InfoBarSeverity.Success);
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

                File.Delete(saveFile.JsonFilePath!);
                ShowStatus("删除成功", $"已删除 {saveFile.DisplayFileName} 的 JSON 文件", InfoBarSeverity.Success);
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

                // 扩展匹配模式，包含备份文件
                var patterns = new[]
                {
                    // 标准存档文件
                    (@"^user(\d+)\.dat$", "dat", "standard"),
                    (@"^user(\d+)\.json$", "json", "standard"),

                    // 版本备份文件
                    (@"^user(\d+)_[\d\.]+\.dat$", "dat", "backup"),

                    // 时间戳备份文件 - 添加这个模式
                    (@"^user(\d+)\.\d{8}_\d{6}\.dat\.bak$", "dat", "backup"),

                    // Mod 文件
                    (@"^user(\d+)\.modded\.json$", "json", "modded"),
                    (@"^user(\d+)\.modded\.json\.bak$", "json", "modded_backup"),

                    // 其他备份文件
                    (@"^user(\d+)\.dat\.bak\d*$", "dat", "backup"),
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

                        // 只设置标准文件路径，忽略备份和 mod 文件
                        if (category == "standard")
                        {
                            if (fileType == "dat")
                            {
                                saveInfo.DatFilePath = filePath;
                            }
                            else if (fileType == "json")
                            {
                                saveInfo.JsonFilePath = filePath;
                            }
                        }

                        // 记录所有相关文件（包括备份文件）
                        if (saveInfo.RelatedFiles == null)
                            saveInfo.RelatedFiles = new List<string>();
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
                    ShowStatus("转换成功", result.Message, InfoBarSeverity.Success);
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
                    ShowStatus("转换成功", result.Message, InfoBarSeverity.Success);
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