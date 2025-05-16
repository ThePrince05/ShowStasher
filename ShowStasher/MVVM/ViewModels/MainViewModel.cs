using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;



namespace ShowStasher.MVVM.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string sourcePath;

        [ObservableProperty]
        private string destinationPath;

        [ObservableProperty]
        private string statusMessage;

        // Holds real-time log messages
        public ObservableCollection<string> LogMessages { get; } = new();

        [RelayCommand]
        private void BrowseSource()
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                SourcePath = dialog.SelectedPath;
                Log("Selected source folder: " + SourcePath);
            }
        }

        [RelayCommand]
        private void BrowseDestination()
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                DestinationPath = dialog.SelectedPath;
                Log("Selected destination folder: " + DestinationPath);
            }
        }

        [RelayCommand]
        private void OrganizeFiles()
        {
            if (string.IsNullOrEmpty(SourcePath) || string.IsNullOrEmpty(DestinationPath))
            {
                StatusMessage = "Please select both source and destination folders.";
                Log("Missing source or destination path.");
                return;
            }

            StatusMessage = "Organizing files...";
            Log("Started organizing...");

            // TODO: Call FileOrganizerService here

            StatusMessage = "Done!";
            Log("Finished organizing files.");
        }

        private void Log(string message)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                LogMessages.Add($"[{DateTime.Now:T}] {message}");
            });
        }
    }
}
