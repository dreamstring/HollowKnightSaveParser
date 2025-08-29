using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace HollowKnightSaveParser.Resources
{
    public static class EmbeddedIconLoader
    {
        public static BitmapImage LoadIcon()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("HollowKnightSaveParser.Assets.icon.ico"))
                {
                    if (stream == null) return null;
                    
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = stream;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}