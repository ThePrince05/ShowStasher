using ShowStasher.MVVM.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ShowStasher.MVVM.Views
{
    /// <summary>
    /// Interaction logic for PreviewDialog.xaml
    /// </summary>
    public partial class PreviewDialog : Window
    {
        public PreviewDialog()
        {
            InitializeComponent();
            this.Closing += PreviewDialog_Closing;
        }

        private readonly Action<string> _log;

        private void PreviewDialog_Closing(object? sender, CancelEventArgs e)
        {
            if (DataContext is PreviewViewModel vm)
            {
                if (!vm.HasResponded)
                {
                    vm.HasResponded = true;
                    vm.OnCancel?.Invoke();
                }
            }

        }
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;
        }

        private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }
}
