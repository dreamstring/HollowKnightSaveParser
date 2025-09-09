using System;
using System.Diagnostics;
using Wpf.Ui.Controls;
using HollowKnightSaveParser.Resources;
using HollowKnightSaveParser.ViewModels;

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
        
        protected override void OnClosed(EventArgs e)
        {
            // 如果你有对 ViewModel 的引用
            if (DataContext is MainViewModel viewModel)
            {
                // 可以调用一个公共的保存方法
                viewModel.SaveSettingsOnExit();
            }
    
            base.OnClosed(e);
        }

    }
}