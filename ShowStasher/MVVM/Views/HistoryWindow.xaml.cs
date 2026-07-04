using ShowStasher.MVVM.ViewModels;
using ShowStasher.Services;
using System;
using System.Windows;
using System.Windows.Input;

namespace ShowStasher.MVVM.Views
{
    /// <summary>
    /// Interaction logic for HistoryWindow.xaml
    /// </summary>
    public partial class HistoryWindow : Window  // Change this from MetroWindow to Window
    {
        public HistoryViewModel ViewModel { get; private set; }

        public HistoryWindow(SqliteDbService dbService)
        {
            InitializeComponent();

            if (dbService == null)
                throw new ArgumentNullException(nameof(dbService), "DbService cannot be null!");

            ViewModel = new HistoryViewModel(dbService);
            DataContext = ViewModel;

            Loaded += async (_, _) => await ViewModel.LoadAsync();
        }

        // Window interaction logic for your custom TitleBar
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