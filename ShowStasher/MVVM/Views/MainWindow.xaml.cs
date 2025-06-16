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
                    Icon = new PackIconMaterial
                    {
                        Kind = PackIconMaterialKind.History,
                        Width = 16,
                        Height = 16,
                        Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#0063B1")),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    Command = vm.OpenHistoryCommand
                };

                var apiKeyItem = new MenuItem
                {
                    Header = "Edit API Key",
                    Icon = new PackIconMaterial
                    {
                        Kind = PackIconMaterialKind.Key,
                        Width = 16,
                        Height = 16,
                        Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#0063B1")),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    Command = vm.OpenApiKeyDialogCommand
                };
                

                menu.Items.Add(historyItem);
                menu.Items.Add(apiKeyItem);

                menu.IsOpen = true;
            }
        }

    }
}