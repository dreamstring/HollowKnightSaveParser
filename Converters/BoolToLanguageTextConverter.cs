using System;
using System.Globalization;
using System.Windows.Data;

namespace HollowKnightSaveParser.Converters
{
    public class BoolToLanguageTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isEnglish)
            {
                return isEnglish ? "中文" : "EN";
            }
            return "中文";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}