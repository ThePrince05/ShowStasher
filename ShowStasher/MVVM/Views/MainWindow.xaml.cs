using MahApps.Metro.Controls;
using ShowStasher.MVVM.ViewModels;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using MahApps.Metro.IconPacks;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;



namespace ShowStasher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            this.StateChanged += MainWindow_StateChanged;
        }


        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                BorderThickness = new Thickness(8);
            }
            else
            {
                BorderThickness = new Thickness(0);
            }
        }
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
           
        }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
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



        private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void OptionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                var menu = new ContextMenu();

                var historyItem = new MenuItem
                {
                    Header = "Open History",
                    Command = vm.OpenHistoryCommand
                };

                var apiKeyItem = new MenuItem
                {
                    Header = "Edit API Key",
                    Command = vm.OpenApiKeyDialogCommand
                };

                var closeAppItem = new MenuItem
                {
                    Header = "Close Application"
                };

                closeAppItem.Click += (s, args) => this.Close();

                menu.Items.Add(historyItem);
                menu.Items.Add(apiKeyItem);
                menu.Items.Add(closeAppItem);

                menu.IsOpen = true;
            }
        }

      
    }
}