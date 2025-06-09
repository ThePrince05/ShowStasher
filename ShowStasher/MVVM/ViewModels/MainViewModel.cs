using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShowStasher.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;



namespace ShowStasher.MVVM.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly FileOrganizerService _fileOrganizerService;
        private readonly MetadataCacheService _metadataCacheService;

        [ObservableProperty]
        private string sourcePath;

        [ObservableProperty]
        private string destinationPath;

        [ObservableProperty]
        private string statusMessage;

        [ObservableProperty]
        private string? selectedLogMessage;

        [ObservableProperty]
        private bool isOfflineMode;

        [ObservableProperty]
        private int progress;

        [ObservableProperty]
        private bool isBusy;




        // Holds real-time log messages
        public ObservableCollection<string> LogMessages { get; } = new();


        public MainViewModel()
        {
            string tmdbApiKey = ConfigurationManager.AppSettings["TMDbApiKey"];

            var cacheService = new MetadataCacheService(Log);

            TMDbService tmdbService = null;

            if (string.IsNullOrWhiteSpace(tmdbApiKey))
            {
                Log("ERROR: TMDb API key is missing in app.config");
                tmdbService = null; // or a fallback instance if you implement one
            }
            else
            {
                tmdbService = new TMDbService(tmdbApiKey, cacheService, Log);
            }

            var jikanService = new JikanService(cacheService,Log);

            // If tmdbService is null, you may want to handle that inside FileOrganizerService
            _fileOrganizerService = new FileOrganizerService(Log, tmdbService, jikanService, cacheService);
        }

        [RelayCommand]
        private void CopyAllLogs()
        {
            if (LogMessages.Count > 0)
            {
                string allLogs = string.Join(Environment.NewLine, LogMessages);
                Clipboard.SetText(allLogs);
                StatusMessage = "Copied all logs to clipboard.";
            }
        }

        [RelayCommand(CanExecute = nameof(CanCopySelectedLog))]
        private void CopySelectedLog()
        {
            if (!string.IsNullOrWhiteSpace(SelectedLogMessage))
            {
                Clipboard.SetText(SelectedLogMessage);
                StatusMessage = "Copied selected log to clipboard.";
            }
        }

        [RelayCommand]
        private void ClearLogs()
        {
            if (LogMessages.Count > 0)
            {
                LogMessages.Clear();
                StatusMessage = "Logs cleared.";
            }
            else
            {
                StatusMessage = "No logs to clear.";
            }
        }

        private bool CanCopySelectedLog() => !string.IsNullOrWhiteSpace(SelectedLogMessage);



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
        private async Task OrganizeFilesAsync()
        {
            if (string.IsNullOrEmpty(SourcePath) || string.IsNullOrEmpty(DestinationPath))
            {
                StatusMessage = "Please select both source and destination folders.";
                Log("Missing source or destination path.");
                return;
            }

            StatusMessage = "Organizing files...";
            Log("Started organizing...");
            IsBusy = true;
            Progress = 0;

            var progressReporter = new Progress<int>(percent =>
            {
                Progress = percent;
            });

            try
            {
                await _fileOrganizerService.OrganizeFilesAsync(SourcePath, DestinationPath, IsOfflineMode, progressReporter);
                StatusMessage = "Done!";
                Log("Finished organizing files.");
            }
            catch (Exception ex)
            {
                StatusMessage = "Error occurred during organizing.";
                Log($"Error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
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
