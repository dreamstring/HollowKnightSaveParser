using System;
using System.IO;
using System.Windows;
using Wpf.Ui.Controls;
using Application = System.Windows.Application;

namespace HollowKnightSaveParser.Views
{
    public partial class TagEditDialog : FluentWindow
    {
        public class TagEditViewModel
        {
            public string FileName { get; set; }
            public string Tag { get; set; }
        }

        public TagEditViewModel ViewModel { get; }
        public bool DialogResult { get; private set; }
        
        // 使用回调函数
        private readonly Action<string, string> _onTagChanged;
        private readonly string _fileName;

        public TagEditDialog(string fileName, string currentTag = null, Action<string, string> onTagChanged = null)
        {
            InitializeComponent();
            
            _fileName = fileName;
            _onTagChanged = onTagChanged;
            
            ViewModel = new TagEditViewModel
            {
                FileName = fileName,
                Tag = currentTag ?? string.Empty
            };
            
            DataContext = ViewModel;
            TagTextBox.Focus();
            TagTextBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var newTag = TagTextBox.Text.Trim();
            
            // 添加验证逻辑
            if (!string.IsNullOrEmpty(newTag))
            {
                // 验证标签长度
                if (newTag.Length > 50)
                {
                    System.Windows.MessageBox.Show(
                        GetString("InvalidTagLength"),
                        GetString("Warning"),
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }
                
                // 检查无效字符
                if (newTag.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    System.Windows.MessageBox.Show(
                        GetString("TagContainsInvalidChars"),
                        GetString("Warning"),
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }
            }
            
            // 通过回调函数通知调用者
            _onTagChanged?.Invoke(_fileName, newTag);
    
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        // 添加获取本地化字符串的辅助方法
        private string GetString(string key)
        {
            return Application.Current.FindResource(key) as string ?? key;
        }
    }
}
