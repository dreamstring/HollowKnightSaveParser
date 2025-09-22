using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;
using HollowKnightSaveParser.Resources;
using HollowKnightSaveParser.ViewModels;
using Application = System.Windows.Application;
using MenuItem = Wpf.Ui.Controls.MenuItem;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace HollowKnightSaveParser.Views
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // 设置窗口图标
            this.Icon = EmbeddedIconLoader.LoadIcon();
            
            // 设置 TitleBar 图标
            SetTitleBarIcon();
        }
        
        private void RefreshContextMenuHeaders(ContextMenu cm)
        {
            foreach (var mi in cm.Items.OfType<MenuItem>())
            {
                if (mi.Tag is string key)
                {
                    // 尝试直接从 Application 资源获取最新值
                    var value = TryFindResource(key) ?? Application.Current.Resources[key];
                    if (value != null)
                        mi.Header = value;
                }
            }
        }
        
        // 代码后置
        private async void OnManualPathLostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.UseManualPath)
            {
                await vm.RefreshSaveDataAsync();
            }
        }
        private void OnGridPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!(e.OriginalSource is TextBox))
            {
                // 使用FocusManager来清除焦点
                FocusManager.SetFocusedElement(this, null);
                Keyboard.ClearFocus();
            }
        }
        
        private void SetTitleBarIcon()
        {
            try
            {
                var iconBitmap = EmbeddedIconLoader.LoadIcon();
                if (iconBitmap != null)
                {
                    // 对于 Wpf.Ui 的 TitleBar，使用 Wpf.Ui.Controls.ImageIcon
                    var imageIcon = new Wpf.Ui.Controls.ImageIcon
                    {
                        Source = iconBitmap
                    };
                    
                    // 设置到 TitleBar
                    MainTitleBar.Icon = imageIcon;
                }
            }
            catch (Exception ex)
            {
                // 如果设置失败，记录日志
                Debug.WriteLine($"设置 TitleBar 图标失败: {ex.Message}");
            }
        }
        
// 在 MainWindow.xaml.cs 中
        protected override void OnClosed(EventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.SaveSettingsOnExit();
            }
            base.OnClosed(e);
        }

    }
}