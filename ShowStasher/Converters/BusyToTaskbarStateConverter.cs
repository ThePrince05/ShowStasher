using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Shell;
namespace ShowStasher.Converters
{
    // Converts bool IsBusy to TaskbarItemProgressState
    public class BusyToTaskbarStateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isBusy && isBusy)
            {
                return TaskbarItemProgressState.Normal;
            }
            return TaskbarItemProgressState.None;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
