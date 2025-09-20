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
        // [ObservableProperty] private bool _isSilksongMode;

        public bool IsSilksongMode => SelectedGameIndex == 1;

        [ObservableProperty] private ObservableCollection<string> _availableSteamIds = new();

        [ObservableProperty] private string? _selectedSteamId;

        [ObservableProperty] private bool _isEnglish = false;

        [ObservableProperty] private string _currentLanguage = "zh-CN";

        public bool HasFiles => SaveFiles.Count > 0;
        public bool HasNoFiles => !IsLoading && SaveFiles.Count == 0;
        public string SaveDirectory { get; private set; } = string.Empty;
        public string FileCountText => string.Format(GetString("FileCountFormat"), SaveFiles.Count);

        private void LoadSettings()
        {
            try
            {
                SelectedGameIndex = Properties.Settings.Default.SelectedGameIndex;
                CurrentLanguage = Properties.Settings.Default.SelectedLanguage ?? "zh-CN";

                // 加载 Steam 相关设置
                if (!string.IsNullOrEmpty(Properties.Settings.Default.SelectedSteamId))
                {
                    SelectedSteamId = Properties.Settings.Default.SelectedSteamId;
                }

                // 应用加载的语言设置
                ApplyLanguage(CurrentLanguage);
            }
            catch (Exception ex)
            {
                // 使用默认值
                SelectedGameIndex = 0;
                CurrentLanguage = "zh-CN";
                SelectedSteamId = null;
                ApplyLanguage(CurrentLanguage);
                System.Diagnostics.Debug.WriteLine($"加载设置失败: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                Properties.Settings.Default.SelectedGameIndex = SelectedGameIndex;
                Properties.Settings.Default.SelectedLanguage = CurrentLanguage;
                Properties.Settings.Default.SelectedSteamId = SelectedSteamId ?? string.Empty;

                // 如果有选中的 Steam 用户，也保存用户ID
                if (SelectedSteamUser != null)
                {
                    Properties.Settings.Default.LastSelectedSteamUserId = SelectedSteamUser.UserId;
                }

                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存设置失败: {ex.Message}");
            }
        }


        public void SaveSettingsOnExit()
        {
            SaveSettings();
        }

        private void ApplyLanguage(string languageCode)
        {
            try
            {
                string resourcePath = languageCode switch
                {
                    "en-US" => "Resources/Strings.en.xaml",
                    _ => "Resources/Strings.xaml"
                };

                LoadLanguageResources(resourcePath);
                IsEnglish = languageCode == "en-US";
            }
            catch (Exception ex)
            {
                // 如果加载失败，使用默认中文
                LoadLanguageResources("Resources/Strings.xaml");
                IsEnglish = false;
            }
        }

        private void LoadLanguageResources(string resourcePath)
        {
            try
            {
                // 移除所有现有的语言资源字典
                var existingLanguageResources = Application.Current.Resources.MergedDictionaries
                    .Where(d => d.Source?.OriginalString?.Contains("Strings") == true)
                    .ToList();

                foreach (var resource in existingLanguageResources)
                {
                    Application.Current.Resources.MergedDictionaries.Remove(resource);
                }

                // 创建新的资源字典
                var newLanguageDict = new ResourceDictionary
                {
                    Source = new Uri(resourcePath, UriKind.Relative)
                };

                // 添加新的语言资源到应用程序资源
                Application.Current.Resources.MergedDictionaries.Add(newLanguageDict);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load language resources from {resourcePath}", ex);
            }
        }

        [RelayCommand]
        private void ToggleLanguage()
        {
            try
            {
                string newCulture;
        
                // 判断当前语言
                var currentLangDict = Application.Current.Resources.MergedDictionaries
                    .FirstOrDefault(d => d.Source?.OriginalString?.Contains("Strings") == true);
            
                if (currentLangDict?.Source?.OriginalString?.Contains("en") == true)
                {
                    // 当前是英文，切换到中文
                    newCulture = "zh-CN";
                    IsEnglish = false;
                }
                else
                {
                    // 当前是中文，切换到英文
                    newCulture = "en-US";
                    IsEnglish = true;
                }

                // 关键：在 Application 级别切换语言资源
                SwitchLanguageResources(newCulture);
                CurrentLanguage = newCulture;

                // 刷新 ViewModel 的本地化属性
                RefreshAllLocalizedProperties();

                ShowStatus("Success", GetString("LanguageSwitched"), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Error", $"Language switch failed: {ex.Message}", InfoBarSeverity.Error);
            }
        }
        
        private void SwitchLanguageResources(string cultureName)
        {
            var app = Application.Current;
    
            // 移除所有现有的语言资源字典
            var oldLangDicts = app.Resources.MergedDictionaries
                .Where(d => d.Source != null && 
                            (d.Source.OriginalString.Contains("Strings.xaml") || 
                             d.Source.OriginalString.Contains("Strings.en.xaml")))
                .ToList();
    
            foreach (var dict in oldLangDicts)
            {
                app.Resources.MergedDictionaries.Remove(dict);
            }

            // 添加新的语言资源字典
            var resourcePath = cultureName == "en-US" ? 
                "Resources/Strings.en.xaml" : 
                "Resources/Strings.xaml";
        
            var newDict = new ResourceDictionary
            {
                Source = new Uri(resourcePath, UriKind.Relative)
            };
    
            app.Resources.MergedDictionaries.Add(newDict);
        }

        private void RefreshAllLocalizedProperties()
        {
            // 刷新主 ViewModel 的属性
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(FileCountText));

            // 刷新所有存档文件的本地化属性
            foreach (var saveFile in SaveFiles)
            {
                saveFile.RefreshLocalizedProperties();
            }

            OnPropertyChanged(nameof(SaveFiles));
        }

        // 获取本地化字符串的辅助方法
        private string GetString(string key)
        {
            return Application.Current.Resources[key] as string ?? key;
        }

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
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        AvailableSteamIds.Clear();
                        SelectedSteamId = null;
                    });
                    ShowStatus(GetString("Info"), GetString("SilksongSaveDirectoryNotFound"), InfoBarSeverity.Warning);
                    return;
                }

                var steamIdDirs = Directory.GetDirectories(silksongBasePath)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrEmpty(name) && IsValidSteamId(name))
                    .OrderBy(id => id)
                    .ToList();

                // 在主线程更新UI
                App.Current.Dispatcher.Invoke(() =>
                {
                    AvailableSteamIds.Clear();
                    foreach (var steamId in steamIdDirs)
                    {
                        AvailableSteamIds.Add(steamId);
                    }

                    // 尝试恢复之前选择的 Steam ID
                    if (AvailableSteamIds.Count > 0)
                    {
                        var savedSteamId = Properties.Settings.Default.SelectedSteamId;
                        if (!string.IsNullOrEmpty(savedSteamId) && AvailableSteamIds.Contains(savedSteamId))
                        {
                            SelectedSteamId = savedSteamId;
                        }
                        else
                        {
                            SelectedSteamId = AvailableSteamIds[0];
                        }
                    }
                    else
                    {
                        SelectedSteamId = null;
                    }
                });

                if (steamIdDirs.Count == 0)
                {
                    ShowStatus(GetString("Info"), GetString("NoSteamUserDirectory"), InfoBarSeverity.Warning);
                }
            }
            catch (Exception ex)
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    AvailableSteamIds.Clear();
                    SelectedSteamId = null;
                });
                ShowStatus(GetString("Error"), string.Format(GetString("LoadSteamIdFailedFormat"), ex.Message),
                    InfoBarSeverity.Error);
            }
        }

        // 验证是否为有效的 Steam ID
        private static bool IsValidSteamId(string steamId)
        {
            return !string.IsNullOrEmpty(steamId) &&
                   steamId.All(char.IsDigit) &&
                   steamId.Length >= 8; // Steam ID 通常比较长
        }

        [RelayCommand]
        private async Task SelectGame(string gameIndex)
        {
            var index = int.Parse(gameIndex);
            SelectedGameIndex = index; // 这会触发 PropertyChanged 事件，从而调用 SaveSettings()
        }


        public void SetLastOperation(string operation, Dictionary<string, object> details)
        {
            LastOperation = operation;
            LastOperationTime = DateTime.Now;
            LastOperationDetails = details ?? new Dictionary<string, object>();
        }

        public MainViewModel()
        {
            // 首先加载设置
            LoadSettings();

            // 监听集合变化
            SaveFiles.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasNoFiles));
                OnPropertyChanged(nameof(HasFiles));
                OnPropertyChanged(nameof(FileCountText));
            };

            // 监听属性变化
            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SelectedGameIndex))
                {
                    OnGameChanged();
                    SaveSettings(); // 保存游戏选择
                }
                else if (e.PropertyName == nameof(SelectedSteamUser))
                {
                    OnSteamUserChanged();
                    SaveSettings(); // 保存 Steam 用户选择
                }
                else if (e.PropertyName == nameof(SelectedSteamId))
                {
                    OnSteamIdChanged();
                    SaveSettings(); // 保存 Steam ID 选择
                }
                else if (e.PropertyName == nameof(CurrentLanguage))
                {
                    SaveSettings(); // 保存语言设置
                }
            };

            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            if (SelectedGameIndex == 1) // 如果是丝之歌模式
            {
                await RefreshSteamUsersAsync(); // 先加载Steam用户
                await LoadAvailableSteamIds(); // 再加载Steam ID
            }

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
            if (SelectedGameIndex == 1) // 丝之歌
            {
                // 先清空，避免显示旧数据
                AvailableSteamIds.Clear();
                SteamUsers.Clear();
                SelectedSteamId = null;
                SelectedSteamUser = null;

                // 按顺序执行异步操作
                _ = Task.Run(async () =>
                {
                    await RefreshSteamUsersAsync(); // 先加载Steam用户
                    await LoadAvailableSteamIds(); // 再加载Steam ID
                });
            }
            else // 空洞骑士
            {
                AvailableSteamIds.Clear();
                SelectedSteamId = null;
                SelectedSteamUser = null;
                InitializeSaveDirectory();
                _ = LoadSaveFilesAsync();
            }
        }

        partial void OnSelectedGameIndexChanged(int value)
        {
            OnPropertyChanged(nameof(IsSilksongMode)); // 通知 IsSilksongMode 属性变化
            OnGameChanged();
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
                var silksongBasePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "..", "LocalLow", "Team Cherry", "Hollow Knight Silksong");
                silksongBasePath = Path.GetFullPath(silksongBasePath);

                if (!Directory.Exists(silksongBasePath))
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        SteamUsers.Clear();
                        SelectedSteamUser = null;
                    });
                    ShowStatus(GetString("Info"), GetString("SilksongSaveDirectoryNotFound"), InfoBarSeverity.Warning);
                    return;
                }

                var userDirectories = Directory.GetDirectories(silksongBasePath)
                    .Where(dir => Regex.IsMatch(Path.GetFileName(dir), @"^\d+$"))
                    .ToArray();

                var steamUsers = new List<SteamUser>();
                foreach (var userDir in userDirectories)
                {
                    var userId = Path.GetFileName(userDir);
                    var steamUser = new SteamUser
                    {
                        UserId = userId,
                        DisplayName = string.Format(GetString("SteamUserFormat"), userId),
                        FolderPath = userDir
                    };
                    steamUsers.Add(steamUser);
                }

                // 在主线程更新UI
                App.Current.Dispatcher.Invoke(() =>
                {
                    SteamUsers.Clear();
                    foreach (var user in steamUsers)
                    {
                        SteamUsers.Add(user);
                    }

                    if (SteamUsers.Count > 0)
                    {
                        // 尝试恢复之前选择的 Steam 用户
                        var lastSelectedUserId = Properties.Settings.Default.LastSelectedSteamUserId;
                        if (!string.IsNullOrEmpty(lastSelectedUserId))
                        {
                            var previousUser = SteamUsers.FirstOrDefault(u => u.UserId == lastSelectedUserId);
                            SelectedSteamUser = previousUser ?? SteamUsers.First();
                        }
                        else
                        {
                            SelectedSteamUser = SteamUsers.First();
                        }
                    }
                    else
                    {
                        SelectedSteamUser = null;
                    }
                });

                if (steamUsers.Count > 0)
                {
                    ShowStatus(GetString("Success"),
                        string.Format(GetString("FoundSteamUsersFormat"), steamUsers.Count), InfoBarSeverity.Success);
                }
                else
                {
                    ShowStatus(GetString("Info"), GetString("NoSteamUserDirectory"), InfoBarSeverity.Warning);
                }
            }
            catch (Exception ex)
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    SteamUsers.Clear();
                    SelectedSteamUser = null;
                });
                ShowStatus(GetString("Error"), string.Format(GetString("RefreshSteamUsersErrorFormat"), ex.Message),
                    InfoBarSeverity.Error);
            }
        }


        private (string Title, string Detail) GetBackupCompleteMessage(string baseDetail)
        {
            if (LastOperationDetails.TryGetValue("sourceFile", out var sourceObj) &&
                LastOperationDetails.TryGetValue("backupFile", out var backupObj))
            {
                var sourceFile = sourceObj.ToString();
                var backupFile = Path.GetFileName(backupObj.ToString());

                return (GetString("BackupComplete"),
                    string.Format(GetString("BackupCompleteDetailFormat"), baseDetail, sourceFile, backupFile));
            }

            return (GetString("BackupComplete"), string.Format(GetString("BackupCompleteGenericFormat"), baseDetail));
        }

        private (string Title, string Detail) GetRestoreCompleteMessage(string baseDetail)
        {
            if (LastOperationDetails.TryGetValue("backupFile", out var backupObj) &&
                LastOperationDetails.TryGetValue("targetFile", out var targetObj))
            {
                var backupFile = Path.GetFileName(backupObj.ToString());
                var targetFile = targetObj.ToString();

                return (GetString("RestoreComplete"),
                    string.Format(GetString("RestoreCompleteDetailFormat"), baseDetail, backupFile, targetFile));
            }

            return (GetString("RestoreComplete"), string.Format(GetString("RestoreCompleteGenericFormat"), baseDetail));
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
                    ShowStatus(GetString("Info"), GetString("SelectSteamUser"), InfoBarSeverity.Warning);
                    return;
                }

                if (!Directory.Exists(SaveDirectory))
                {
                    ShowStatus(GetString("Error"), GetString("SaveDirNotFound"), InfoBarSeverity.Error);
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
                    ShowStatus(GetString("Info"), GetString("NoSaveFiles"), InfoBarSeverity.Warning);
                }
            }
            catch (Exception ex)
            {
                ShowStatus(GetString("LoadFailed"), ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private (string Title, string Detail) GetLoadCompleteMessage()
        {
            var fileCount = SaveFiles.Count;
            var baseDetail = string.Format(GetString("FileCountFormat"), fileCount);

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
                    _ => (GetString("RefreshComplete"), baseDetail)
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
                details.Add(string.Format(GetString("BackupFilesDetectedFormat"), totalBackups));
            }

            if (modCount > 0)
            {
                details.Add(string.Format(GetString("ModdedSavesFormat"), modCount));
            }

            if (jsonCount > 0 && datCount > 0)
            {
                details.Add(string.Format(GetString("DatJsonCountFormat"), datCount, jsonCount));
            }
            else if (jsonCount > 0)
            {
                details.Add(string.Format(GetString("JsonOnlyFormat"), jsonCount));
            }
            else if (datCount > 0)
            {
                details.Add(string.Format(GetString("DatOnlyFormat"), datCount));
            }

            return (GetString("LoadComplete"), string.Join("，", details));
        }

        private (string Title, string Detail) GetConvertCompleteMessage(string targetFormat, string baseDetail)
        {
            if (LastOperationDetails.TryGetValue("sourceFile", out var sourceObj) &&
                LastOperationDetails.TryGetValue("targetFile", out var targetObj))
            {
                var sourceFile = sourceObj.ToString();
                var targetFile = targetObj.ToString();

                return (GetString("ConvertComplete"),
                    string.Format(GetString("ConvertCompleteDetailFormat"), baseDetail, sourceFile, targetFile));
            }

            return (GetString("ConvertComplete"),
                $"{baseDetail}，{(targetFormat == "JSON" ? GetString("DatToJsonConverted") : GetString("JsonToDatConverted"))}");
        }

        private (string Title, string Detail) GetDeleteCompleteMessage(string fileType, string baseDetail)
        {
            if (LastOperationDetails.TryGetValue("deletedFile", out var deletedObj))
            {
                var deletedFile = deletedObj.ToString();
                return (GetString("DeleteComplete"),
                    string.Format(GetString("DeleteCompleteDetailFormat"), baseDetail, deletedFile));
            }

            return (GetString("DeleteComplete"),
                string.Format(GetString("FileTypeDeletedFormat"), baseDetail, fileType));
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
                ShowStatus(GetString("BackupFailed"),
                    string.Format(GetString("BackupErrorFormat"), saveFile.DisplayFileName, ex.Message),
                    InfoBarSeverity.Error);
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
                    ShowStatus(GetString("RestoreFailed"), GetString("NoValidBackupFile"), InfoBarSeverity.Error);
                    return;
                }

                // 弹出确认对话框
                var result = await ShowConfirmationDialogAsync(
                    GetString("ConfirmRestore"),
                    string.Format(GetString("ConfirmRestoreMessagePart1"), saveFile.DisplayFileName) +
                    string.Format(GetString("BackupFileInfo"), Path.GetFileName(backupPath)) +
                    GetString("RestoreWarning"));

                if (!result) return;

                ShowStatus(GetString("Restoring"),
                    string.Format(GetString("RestoringFormat"), saveFile.DisplayFileName),
                    InfoBarSeverity.Informational);

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
                ShowStatus(GetString("RestoreFailed"),
                    string.Format(GetString("RestoreErrorFormat"), saveFile.DisplayFileName, ex.Message),
                    InfoBarSeverity.Error);
            }
        }


        [RelayCommand]
        private async Task DeleteDatFileAsync(SaveFileInfo? saveFile)
        {
            if (saveFile == null || !saveFile.HasDatFile) return;

            try
            {
                var result = await ShowConfirmationDialogAsync(
                    GetString("ConfirmDelete"),
                    string.Format(GetString("ConfirmDeleteDatMessagePart1"), saveFile.DisplayFileName) +
                    string.Format(GetString("FilePathInfo"), saveFile.DatFilePath) +
                    GetString("IrreversibleWarning"));

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
                ShowStatus(GetString("DeleteFailed"), string.Format(GetString("DeleteDatErrorFormat"), ex.Message),
                    InfoBarSeverity.Error);
            }
        }

        [RelayCommand]
        private async Task DeleteJsonFileAsync(SaveFileInfo? saveFile)
        {
            if (saveFile == null || !saveFile.HasJsonFile) return;

            try
            {
                var result = await ShowConfirmationDialogAsync(
                    GetString("ConfirmDelete"),
                    string.Format(GetString("ConfirmDeleteJsonMessagePart1"), saveFile.DisplayFileName) +
                    string.Format(GetString("FilePathInfo"), saveFile.JsonFilePath) +
                    GetString("IrreversibleWarning"));

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
                ShowStatus(GetString("DeleteFailed"), string.Format(GetString("DeleteJsonErrorFormat"), ex.Message),
                    InfoBarSeverity.Error);
            }
        }


        // 确认对话框方法 - 简化版本
        private async Task<bool> ShowConfirmationDialogAsync(string title, string message)
        {
            var textBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 400,
                LineHeight = 20,
                Margin = new Thickness(0, 10, 0, 10)
            };

            var dialog = new Wpf.Ui.Controls.MessageBox
            {
                Title = title,
                Content = textBlock,
                PrimaryButtonText = GetString("Confirm"),
                CloseButtonText = GetString("Cancel"),
                Owner = Application.Current.MainWindow,
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

            var result = saveFileGroups.Values.OrderBy(s => s.SlotNumber).ToArray();
            foreach (var saveFile in result)
            {
                saveFile.RefreshBackupVersions(); // 在这里调用
            }
    
            return result;
        }

        [RelayCommand]
        private async Task ConvertDatToJsonAsync(SaveFileInfo? saveFile)
        {
            if (saveFile == null || !saveFile.HasDatFile) return;

            try
            {
                ShowStatus(GetString("Converting"),
                    string.Format(GetString("ConvertingToJsonFormat"), saveFile.DisplayFileName),
                    InfoBarSeverity.Informational);

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
                    ShowStatus(GetString("ConvertFailed"), result.Message, InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                ShowStatus(GetString("ConvertFailed"),
                    string.Format(GetString("ConvertErrorFormat"), saveFile.DisplayFileName, ex.Message),
                    InfoBarSeverity.Error);
            }
        }

        [RelayCommand]
        private async Task ConvertJsonToDatAsync(SaveFileInfo? saveFile)
        {
            if (saveFile == null || !saveFile.HasJsonFile) return;

            try
            {
                ShowStatus(GetString("Converting"),
                    string.Format(GetString("ConvertingToDatFormat"), saveFile.DisplayFileName),
                    InfoBarSeverity.Informational);

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
                    ShowStatus(GetString("ConvertFailed"), result.Message, InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                ShowStatus(GetString("ConvertFailed"),
                    string.Format(GetString("ConvertErrorFormat"), saveFile.DisplayFileName, ex.Message),
                    InfoBarSeverity.Error);
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
                    ShowStatus(GetString("Error"), GetString("SaveDirNotFound"), InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                ShowStatus(GetString("OpenFailed"), ex.Message, InfoBarSeverity.Error);
            }
        }

        #region 右键菜单命令

        private ICommand? _createBackupCommand;

        public ICommand CreateBackupCommand => _createBackupCommand ??= new RelayCommand<SaveFileInfo>(
            async saveFile => await CreateBackupAsync(saveFile),
            saveFile => saveFile?.CanBackup == true);

        private ICommand? _restoreFromBackupCommand;

        public ICommand RestoreFromBackupCommand => _restoreFromBackupCommand ??= new RelayCommand<BackupFileInfo>(
            async backupFile => await RestoreFromBackupAsync(backupFile),
            backupFile => backupFile != null);

        private ICommand? _deleteBackupCommand;

        public ICommand DeleteBackupCommand => _deleteBackupCommand ??= new RelayCommand<BackupFileInfo>(
            async backupFile => await DeleteBackupAsync(backupFile),
            backupFile => backupFile != null);

        private ICommand? _openInExplorerCommand;

        public ICommand OpenInExplorerCommand => _openInExplorerCommand ??= new RelayCommand<SaveFileInfo>(
            saveFile => OpenInExplorer(saveFile),
            saveFile => saveFile != null && !string.IsNullOrEmpty(saveFile.DirectoryPath));

        private ICommand? _copyFilePathCommand;

        public ICommand CopyFilePathCommand => _copyFilePathCommand ??= new RelayCommand<SaveFileInfo>(
            saveFile => CopyFilePath(saveFile),
            saveFile => saveFile != null && !string.IsNullOrEmpty(saveFile.PrimaryFilePath));

        private ICommand? _renameFileCommand;

        public ICommand RenameFileCommand => _renameFileCommand ??= new RelayCommand<SaveFileInfo>(
            async saveFile => await RenameFileAsync(saveFile),
            saveFile => saveFile != null && saveFile.IsValid);

        private ICommand? _deleteFileCommand;

        public ICommand DeleteFileCommand => _deleteFileCommand ??= new RelayCommand<SaveFileInfo>(
            async saveFile => await DeleteFileAsync(saveFile),
            saveFile => saveFile != null && saveFile.IsValid);

        private ICommand? _refreshBackupsCommand;

        public ICommand RefreshBackupsCommand => _refreshBackupsCommand ??= new RelayCommand<SaveFileInfo>(
            saveFile => RefreshBackups(saveFile),
            saveFile => saveFile != null);
        
        private ICommand? _restoreFromSpecificBackupCommand;
        public ICommand RestoreFromSpecificBackupCommand => _restoreFromSpecificBackupCommand ??= new RelayCommand<BackupFileInfo>(
            async backupFile => await RestoreFromBackupAsync(backupFile),
            backupFile => backupFile != null);

        private ICommand? _deleteSpecificBackupCommand;
        public ICommand DeleteSpecificBackupCommand => _deleteSpecificBackupCommand ??= new RelayCommand<BackupFileInfo>(
            async backupFile => await DeleteBackupAsync(backupFile),
            backupFile => backupFile != null);

        private ICommand? _cleanupOldBackupsCommand;
        public ICommand CleanupOldBackupsCommand => _cleanupOldBackupsCommand ??= new RelayCommand<SaveFileInfo>(
            async saveFile => await CleanupOldBackupsAsync(saveFile),
            saveFile => saveFile?.HasBackups == true);


        #endregion

        private void ShowStatus(string messageKey, string detail, InfoBarSeverity severity)
        {
            StatusMessage = GetString(messageKey);
            StatusDetail = detail; // 详细信息可以保持动态生成
            StatusSeverity = severity;
            IsStatusVisible = true;

            if (severity == InfoBarSeverity.Success || severity == InfoBarSeverity.Informational)
            {
                Task.Delay(3000).ContinueWith(_ => { App.Current.Dispatcher.Invoke(() => IsStatusVisible = false); });
            }
        }

        #region 右键菜单方法实现

        // 创建备份
        private async Task CreateBackupAsync(SaveFileInfo saveFile)
        {
            try
            {
                if (!saveFile.HasDatFile)
                {
                    ShowStatus("Error", GetString("NoDatFileForBackup"), InfoBarSeverity.Error);
                    return;
                }

                var backupPath = saveFile.GenerateBackupFilePath();

                // 复制文件创建备份
                File.Copy(saveFile.DatFilePath, backupPath, false);

                // 记录操作信息
                SetLastOperation("backup", new Dictionary<string, object>
                {
                    ["sourceFile"] = Path.GetFileName(saveFile.DatFilePath),
                    ["backupFile"] = backupPath
                });

                // 刷新相关文件列表
                await LoadSaveFilesAsync();
            }
            catch (Exception ex)
            {
                ShowStatus("Error", string.Format(GetString("CreateBackupErrorFormat"), ex.Message),
                    InfoBarSeverity.Error);
            }
        }

        // 从备份恢复
        private async Task RestoreFromBackupAsync(BackupFileInfo backupFile)
        {
            try
            {
                // 找到对应的存档文件
                var saveFile = SaveFiles.FirstOrDefault(sf =>
                    Path.GetDirectoryName(sf.PrimaryFilePath) == Path.GetDirectoryName(backupFile.FilePath));

                if (saveFile == null)
                {
                    ShowStatus("Error", GetString("CannotFindCorrespondingSaveFile"), InfoBarSeverity.Error);
                    return;
                }

                // 确认对话框
                var result = await ShowConfirmationDialogAsync(
                    GetString("ConfirmRestore"),
                    string.Format(GetString("ConfirmRestoreFromBackupFormat"),
                        backupFile.DisplayName, saveFile.DisplayFileName));

                if (!result) return;

                // 创建恢复前备份
                if (saveFile.HasDatFile)
                {
                    var beforeRestorePath = saveFile.GenerateBeforeRestoreBackupPath();
                    File.Copy(saveFile.DatFilePath, beforeRestorePath, true);
                }

                // 执行恢复
                var targetPath = saveFile.HasDatFile
                    ? saveFile.DatFilePath
                    : Path.Combine(saveFile.DirectoryPath, $"{saveFile.BaseName}.dat");

                File.Copy(backupFile.FilePath, targetPath, true);

                // 如果存在 JSON 文件，删除它（因为它可能与恢复的 DAT 不匹配）
                if (saveFile.HasJsonFile && File.Exists(saveFile.JsonFilePath))
                {
                    File.Delete(saveFile.JsonFilePath);
                }

                // 记录操作信息
                SetLastOperation("restore", new Dictionary<string, object>
                {
                    ["backupFile"] = backupFile.FilePath,
                    ["targetFile"] = Path.GetFileName(targetPath)
                });

                // 刷新文件列表
                await LoadSaveFilesAsync();
            }
            catch (Exception ex)
            {
                ShowStatus("Error", string.Format(GetString("RestoreFromBackupErrorFormat"), ex.Message),
                    InfoBarSeverity.Error);
            }
        }

        // 删除备份
        private async Task DeleteBackupAsync(BackupFileInfo backupFile)
        {
            try
            {
                var result = await ShowConfirmationDialogAsync(
                    GetString("ConfirmDelete"),
                    string.Format(GetString("ConfirmDeleteBackupFormat"), backupFile.DisplayName));

                if (!result) return;

                File.Delete(backupFile.FilePath);

                // 刷新文件列表
                await LoadSaveFilesAsync();

                ShowStatus("Success", GetString("BackupDeleted"), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Error", string.Format(GetString("DeleteBackupErrorFormat"), ex.Message),
                    InfoBarSeverity.Error);
            }
        }

        // 在资源管理器中打开
        private void OpenInExplorer(SaveFileInfo saveFile)
        {
            try
            {
                if (Directory.Exists(saveFile.DirectoryPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{saveFile.PrimaryFilePath}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                ShowStatus("Error", string.Format(GetString("OpenExplorerErrorFormat"), ex.Message),
                    InfoBarSeverity.Error);
            }
        }

        // 复制文件路径
        private void CopyFilePath(SaveFileInfo saveFile)
        {
            try
            {
                Clipboard.SetText(saveFile.PrimaryFilePath);
                ShowStatus("Success", GetString("FilePathCopied"), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Error", string.Format(GetString("CopyPathErrorFormat"), ex.Message), InfoBarSeverity.Error);
            }
        }

        // 重命名文件
        private async Task RenameFileAsync(SaveFileInfo saveFile)
        {
            try
            {
                // 使用简单的输入对话框
                var newName = Microsoft.VisualBasic.Interaction.InputBox(
                    GetString("EnterNewFileName"),
                    GetString("RenameFile"),
                    saveFile.BaseName);

                if (string.IsNullOrWhiteSpace(newName) || newName == saveFile.BaseName)
                    return;

                // 验证文件名
                if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    ShowStatus("Error", GetString("InvalidFileName"), InfoBarSeverity.Error);
                    return;
                }

                var directory = saveFile.DirectoryPath;

                // 重命名 DAT 文件
                if (saveFile.HasDatFile)
                {
                    var newDatPath = Path.Combine(directory, $"{newName}.dat");
                    if (File.Exists(newDatPath))
                    {
                        ShowStatus("Error", GetString("TargetFileExists"), InfoBarSeverity.Error);
                        return;
                    }

                    File.Move(saveFile.DatFilePath, newDatPath);
                }

                // 重命名 JSON 文件
                if (saveFile.HasJsonFile)
                {
                    var newJsonPath = Path.Combine(directory, $"{newName}.json");
                    if (File.Exists(newJsonPath))
                    {
                        ShowStatus("Error", GetString("TargetJsonFileExists"), InfoBarSeverity.Error);
                        return;
                    }

                    File.Move(saveFile.JsonFilePath, newJsonPath);
                }

                // 刷新文件列表
                await LoadSaveFilesAsync();

                ShowStatus("Success", GetString("FileRenamed"), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Error", string.Format(GetString("RenameErrorFormat"), ex.Message), InfoBarSeverity.Error);
            }
        }

        // 删除文件
        private async Task DeleteFileAsync(SaveFileInfo saveFile)
        {
            try
            {
                var filesToDelete = new List<string>();
                if (saveFile.HasDatFile) filesToDelete.Add(GetString("DatFile"));
                if (saveFile.HasJsonFile) filesToDelete.Add(GetString("JsonFile"));

                var result = await ShowConfirmationDialogAsync(
                    GetString("ConfirmDelete"),
                    string.Format(GetString("ConfirmDeleteFilesFormat"),
                        string.Join(GetString("And"), filesToDelete)));

                if (!result) return;

                // 删除文件
                if (saveFile.HasDatFile && File.Exists(saveFile.DatFilePath))
                {
                    File.Delete(saveFile.DatFilePath);
                }

                if (saveFile.HasJsonFile && File.Exists(saveFile.JsonFilePath))
                {
                    File.Delete(saveFile.JsonFilePath);
                }

                // 刷新文件列表
                await LoadSaveFilesAsync();

                ShowStatus("Success", GetString("FilesDeleted"), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Error", string.Format(GetString("DeleteFileErrorFormat"), ex.Message),
                    InfoBarSeverity.Error);
            }
        }

        // 刷新备份列表
        private void RefreshBackups(SaveFileInfo saveFile)
        {
            try
            {
                saveFile.RefreshBackupVersions();
                ShowStatus("Success", GetString("BackupListRefreshed"), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Error", string.Format(GetString("RefreshBackupErrorFormat"), ex.Message),
                    InfoBarSeverity.Error);
            }
        }
        
        // 清理旧备份
        private async Task CleanupOldBackupsAsync(SaveFileInfo saveFile)
        {
            try
            {
                var backups = saveFile.BackupVersions.ToList();
                if (backups.Count <= 5) // 保留最新的5个备份
                {
                    ShowStatus("Info", GetString("NoOldBackupsToCleanup"), InfoBarSeverity.Informational);
                    return;
                }

                var toDelete = backups.Skip(5).ToList(); // 删除除最新5个之外的备份
        
                var result = await ShowConfirmationDialogAsync(
                    GetString("ConfirmCleanup"),
                    string.Format(GetString("ConfirmCleanupOldBackupsFormat"), toDelete.Count));

                if (!result) return;

                foreach (var backup in toDelete)
                {
                    if (File.Exists(backup.FilePath))
                    {
                        File.Delete(backup.FilePath);
                    }
                }

                // 刷新文件列表
                await LoadSaveFilesAsync();
        
                ShowStatus("Success", string.Format(GetString("CleanupCompleteFormat"), toDelete.Count), InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("Error", string.Format(GetString("CleanupErrorFormat"), ex.Message), InfoBarSeverity.Error);
            }
        }


        #endregion
    }
}