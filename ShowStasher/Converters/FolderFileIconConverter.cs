using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Application = System.Windows.Application;

namespace ShowStasher.Converters
{
    public class FolderFileIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isFolder = value is bool b && b;
            string key = isFolder ? "FolderIcon" : "FileIcon";

            var icon = Application.Current.TryFindResource(key) as ImageSource;

            if (icon == null)
            {
                System.Diagnostics.Debug.WriteLine($"[Converter] Value: {value}, Parameter: {parameter}");
            }

            return icon;
        }


        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

}
