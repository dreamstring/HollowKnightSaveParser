using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace HollowKnightSaveParser.Converters
{
    public class FileStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string status)
            {
                return status switch
                {
                    "DAT + JSON" => new SolidColorBrush(Color.FromRgb(34, 139, 34)),   // 森林绿 - 完整
                    "JSON + DAT" => new SolidColorBrush(Color.FromRgb(34, 139, 34)),   // 森林绿 - 完整
                    "仅 DAT" => new SolidColorBrush(Color.FromRgb(30, 144, 255)),      // 道奇蓝 - DAT
                    "仅 JSON" => new SolidColorBrush(Color.FromRgb(255, 140, 0)),      // 深橙色 - JSON
                    _ => new SolidColorBrush(Color.FromRgb(128, 128, 128))             // 灰色 - 未知
                };
            }
            return new SolidColorBrush(Color.FromRgb(128, 128, 128));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}