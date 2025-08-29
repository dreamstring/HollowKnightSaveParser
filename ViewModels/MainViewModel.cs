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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HollowKnightSaveParser.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly SaveFileService _saveFileService = new();

        [ObservableProperty]
        private ObservableCollection<SaveFileInfo> _saveFiles = new();

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _isStatusVisible;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private string _statusDetail = string.Empty;

        [ObservableProperty]
        private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;

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
    
            InitializeSaveDirectory();
            _ = LoadSaveFilesAsync();
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

        private void InitializeSaveDirectory()
        {
            var localLowPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "..", "LocalLow", "Team Cherry", "Hollow Knight");
            localLowPath = Path.GetFullPath(localLowPath);
    
            SaveDirectory = Directory.Exists(localLowPath) ? localLowPath : string.Empty;
        }


        [RelayCommand]
        private async Task LoadSaveFilesAsync()
        {
            IsLoading = true;
            SaveFiles.Clear();

            try
            {
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

        private SaveFileInfo[] GroupSaveFiles()
        {
            if (!Directory.Exists(SaveDirectory))
                return Array.Empty<SaveFileInfo>();

            var allFiles = Directory.GetFiles(SaveDirectory, "*", SearchOption.TopDirectoryOnly);

            // 更严格的正则表达式，确保只匹配真正的存档文件
            var saveFilePatterns = new[]
            {
                new Regex(@"^user(\d+)\.dat$", RegexOptions.IgnoreCase), // user1.dat
                new Regex(@"^user(\d+)\.json$", RegexOptions.IgnoreCase), // user1.json  
                new Regex(@"^user(\d+)\..*\.dat$", RegexOptions.IgnoreCase), // user1.1.4.3.2.dat
                new Regex(@"^user(\d+)\..*\.json$", RegexOptions.IgnoreCase), // user1.modded.json
            };

            var saveFileGroups = allFiles
                .Select(file =>
                {
                    var fileName = Path.GetFileName(file);
                    foreach (var pattern in saveFilePatterns)
                    {
                        var match = pattern.Match(fileName);
                        if (match.Success)
                        {
                            var slotNumber = int.Parse(match.Groups[1].Value);
                            var extension = Path.GetExtension(fileName).ToLowerInvariant();

                            return new
                            {
                                FilePath = file,
                                FileName = fileName,
                                SlotNumber = slotNumber,
                                Extension = extension,
                                IsValid = true
                            };
                        }
                    }

                    return new
                    {
                        FilePath = file, FileName = fileName, SlotNumber = 0, Extension = "", IsValid = false
                    };
                })
                .Where(f => f.IsValid)
                .GroupBy(f => f.SlotNumber)
                .Select(group =>
                {
                    var saveInfo = new SaveFileInfo
                    {
                        SlotNumber = group.Key,
                        BaseName = $"user{group.Key}" // 设置基础名称
                    };

                    foreach (var file in group)
                    {
                        if (file.Extension == ".dat")
                        {
                            saveInfo.DatFilePath = file.FilePath;
                        }
                        else if (file.Extension == ".json")
                        {
                            saveInfo.JsonFilePath = file.FilePath;
                        }
                    }

                    // 如果只有一个文件，使用完整的文件名作为基础名称
                    if (group.Count() == 1)
                    {
                        var singleFile = group.First();
                        saveInfo.BaseName = Path.GetFileNameWithoutExtension(singleFile.FileName);
                    }

                    return saveInfo;
                })
                .ToArray();

            return saveFileGroups;
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
                Task.Delay(3000).ContinueWith(_ => 
                { 
                    App.Current.Dispatcher.Invoke(() => IsStatusVisible = false); 
                });
            }
        }
    }
}
