using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Shell;

namespace ShowStasher.Converters
{
    // Converts 0-100 int to 0.0-1.0 double
    public class ProgressToTaskbarValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue / 100.0;
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

   
}