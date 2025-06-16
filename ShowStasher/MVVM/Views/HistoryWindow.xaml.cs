using MahApps.Metro.Controls;
using ShowStasher.MVVM.ViewModels;
using ShowStasher.Services;
using System;
using System.Collections.Generic;
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
    /// Interaction logic for HistoryWindow.xaml
    /// </summary>
    public partial class HistoryWindow : MetroWindow
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
    }
}
