using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShowStasher.MVVM.Models;
using ShowStasher.MVVM.Views;
using ShowStasher.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Linq;
using System.Windows;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Clipboard = System.Windows.Clipboard;
using Application = System.Windows.Application;
using System.IO;
using System.Diagnostics;



namespace ShowStasher.MVVM.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private FileOrganizerService _fileOrganizerService;
        private readonly SqliteDbService _dbService;

        [ObservableProperty] private string sourcePath;
        [ObservableProperty] private string destinationPath;
        [ObservableProperty] private string statusMessage;
        [ObservableProperty] private string? selectedLogMessage;
        [ObservableProperty] private bool isOfflineMode;
        [ObservableProperty] private int progress;
        [ObservableProperty] private bool isBusy;

        public ObservableCollection<string> LogMessages { get; } = new();
        public ObservableCollection<string> SelectedLogMessages { get; } = new();

        public MainViewModel()
        {
            _dbService = new SqliteDbService(Log);

            Task.Run(async () =>
            {
                string tmdbApiKey = await EnsureTmdbApiKeyAsync();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    InitializeServices(tmdbApiKey);
                });
            });
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
            if (SelectedLogMessages.Any())
            {
                var combined = string.Join(Environment.NewLine, SelectedLogMessages);
                Clipboard.SetText(combined);
                StatusMessage = "Copied selected logs to clipboard.";
            }
        }

        private bool CanCopySelectedLog() => SelectedLogMessages.Any();

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
        private async Task PreviewAndOrganizeAsync()
        {
            if (string.IsNullOrEmpty(SourcePath) || string.IsNullOrEmpty(DestinationPath))
            {
                StatusMessage = "Please select both source and destination folders.";
                Log("Missing source or destination path.", AppLogLevel.Error);
                return;
            }

            StatusMessage = "Preparing preview...";
            Log("Generating dry run preview...", AppLogLevel.Action);
            IsBusy = true;
            Progress = 0;

            try
            {
                var previewItems = await _fileOrganizerService.GetDryRunTreeAsync(SourcePath, IsOfflineMode);
                var previewViewModel = new PreviewViewModel();

                previewViewModel.RootItems.Clear();
                foreach (var item in previewItems)
                    previewViewModel.RootItems.Add(item);

                var dialog = new PreviewDialog
                {
                    DataContext = previewViewModel,
                    Owner = Application.Current.MainWindow
                };

                previewViewModel.RequestClose = () => dialog.Close();

                var tcs = new TaskCompletionSource<IList<PreviewItem>>();

                previewViewModel.OnConfirm = _ =>
                {
                    var checkedFiles = previewViewModel.GetCheckedFiles(previewViewModel.RootItems);
                    tcs.TrySetResult(checkedFiles);
                };

                previewViewModel.OnCancel = () =>
                {
                    tcs.TrySetResult(new List<PreviewItem>());
                };

                dialog.ShowDialog();
                var selectedFilesToOrganize = await tcs.Task;

                if (selectedFilesToOrganize.Count == 0)
                {
                    StatusMessage = "Organization canceled.";
                    Log("Organization canceled by user (no files selected).", AppLogLevel.Warning);
                    return;
                }

                StatusMessage = "Organizing selected files...";
                Log($"Starting organization of {selectedFilesToOrganize.Count} files...", AppLogLevel.Action);

                var progressReporter = new Progress<int>(percent => Progress = percent);

                await _fileOrganizerService.OrganizeFilesAsync(
                    selectedFilesToOrganize, DestinationPath, IsOfflineMode, progressReporter);

                StatusMessage = "Done!";
                Log("Finished organizing selected files.", AppLogLevel.Success);
            }
            catch (Exception ex)
            {
                StatusMessage = "Error occurred during organizing.";
                Log($"Error during organization: {ex.Message}", AppLogLevel.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }


        [RelayCommand]
        private async Task OpenHistoryAsync()
        {
            var historyWindow = new HistoryWindow(_dbService);
            historyWindow.Show();
        }

        [RelayCommand]
        private void OpenApiKeyDialog()
        {
            var window = new TmdbApiKeyWindow
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Topmost = true
            };

            var viewModel = new TmdbApiKeyViewModel(_dbService, window);
            window.DataContext = viewModel;

            window.Show();
        }


        private void InitializeServices(string tmdbApiKey)
        {
            var cacheService = new SqliteDbService(Log);
            var selectionService = new MetadataSelectionService();

            TMDbService tmdbService = null;
            if (string.IsNullOrWhiteSpace(tmdbApiKey))
            {
                Log("TMDb API key is missing.", AppLogLevel.Error);
            }
            else
            {
                tmdbService = new TMDbService(tmdbApiKey, cacheService, Log, selectionService);
                Log("TMDb Service initialized successfully.", AppLogLevel.Success);
            }

            var jikanService = new JikanService(cacheService, Log);

            var displayTitleResolverService = new DisplayTitleResolverService(tmdbService, jikanService, Log);
            _fileOrganizerService = new FileOrganizerService(Log, tmdbService, jikanService, cacheService, displayTitleResolverService);

            Log("Services initialized.", AppLogLevel.Info);
        }


        private async Task<string> EnsureTmdbApiKeyAsync()
        {
            string apiKey = await _dbService.GetSettingAsync("TMDbApiKey");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                await Task.Delay(2000); // Let MainWindow fully render

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var window = new TmdbApiKeyWindow
                    {
                        Owner = Application.Current.MainWindow,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Topmost = true
                    };

                    var viewModel = new TmdbApiKeyViewModel(_dbService, window);
                    window.DataContext = viewModel;

                    window.Activate();
                    window.ShowDialog();
                });

                apiKey = await _dbService.GetSettingAsync("TMDbApiKey");

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    Log("ERROR: TMDb API key is still missing after prompt.");
                }
            }

            return apiKey;
        }


        public enum AppLogLevel
        {
            Info,
            Success,
            Warning,
            Error,
            Debug,
            Action
        }

        private void Log(string message, AppLogLevel level = AppLogLevel.Info)
        {
            string prefix = level switch
            {
                AppLogLevel.Info => "ℹ️",
                AppLogLevel.Success => "✅",
                AppLogLevel.Warning => "⚠️",
                AppLogLevel.Error => "❌",
                AppLogLevel.Debug => "[DEBUG]",
                AppLogLevel.Action => "🔄",
                _ => ""
            };

            var timestamped = $"[{DateTime.Now:T}] {prefix} {message}";

            if (Application.Current?.Dispatcher?.HasShutdownStarted == false)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    LogMessages.Add(timestamped);
                });
            }
            else
            {
                Debug.WriteLine($"[Log Skipped] {timestamped}");
            }
        }


    }

}
