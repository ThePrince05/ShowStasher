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
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;


namespace ShowStasher.MVVM.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private FileOrganizerService _fileOrganizerService;
        private readonly SqliteDbService _dbService;

        [ObservableProperty] private string sourcePath;
        [ObservableProperty] private string destinationPath;
        [ObservableProperty] private string statusMessage;
        [ObservableProperty]
        private LogEntry? selectedLogMessage;

        [ObservableProperty] private bool isOfflineMode;
        [ObservableProperty] private int progress;
        [ObservableProperty] private bool isBusy;

        public ObservableCollection<LogEntry> LogMessages { get; } = new();
        public ObservableCollection<LogEntry> SelectedLogMessages { get; } = new();


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

        public class LogEntry
        {
            public string Timestamp { get; set; }
            public string Level { get; set; }
            public string Message { get; set; }

            public string LevelColor { get; set; }   // <-- string instead of Brush
            public string Icon { get; set; }
        }
        private void SetStatusMessage(string message)
        {
            StatusMessage = $"Status: {message}";
        }

        [RelayCommand]
        private void CopyAllLogs()
        {
            if (LogMessages.Count > 0)
            {
                string allLogs = string.Join(
                    Environment.NewLine,
                    LogMessages.Select(x =>
                        $"{x.Timestamp} {x.Icon} {x.Level} {x.Message}")
                );
                Clipboard.SetText(allLogs);
                SetStatusMessage("Copied all logs to clipboard.");
            }
        }

        [RelayCommand(CanExecute = nameof(CanCopySelectedLog))]
        private void CopySelectedLog()
        {
            if (SelectedLogMessages.Any())
            {
                var combined = string.Join(
                Environment.NewLine,
                SelectedLogMessages.Select(x =>
                    $"{x.Timestamp} {x.Icon} {x.Level} {x.Message}")
            );
                Clipboard.SetText(combined);
                SetStatusMessage("Copied selected logs to clipboard."); // Fixed
            }
        }

        private bool CanCopySelectedLog() => SelectedLogMessages.Any();

        [RelayCommand]
        private void ClearLogs()
        {
            if (LogMessages.Count > 0)
            {
                LogMessages.Clear();
                SetStatusMessage("Logs cleared."); // Fixed
            }
            else
            {
                SetStatusMessage("No logs to clear."); // Fixed
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
                SetStatusMessage("Please select both source and destination folders."); // Fixed
                Log("Missing source or destination path.", AppLogLevel.Error);
                return;
            }

            SetStatusMessage("Preparing preview..."); // Fixed
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
                    SetStatusMessage("Organization canceled."); // Fixed
                    Log("Organization canceled by user (no files selected).", AppLogLevel.Warning);
                    return;
                }

                SetStatusMessage("Organizing selected files..."); // Fixed
                Log($"Starting organization of {selectedFilesToOrganize.Count} files...", AppLogLevel.Action);

                var progressReporter = new Progress<int>(percent => Progress = percent);

                await _fileOrganizerService.OrganizeFilesAsync(
                    selectedFilesToOrganize, DestinationPath, IsOfflineMode, progressReporter);

                SetStatusMessage("Done!"); // Fixed
                Log("Finished organizing selected files.", AppLogLevel.Success);
            }
            catch (Exception ex)
            {
                SetStatusMessage("Error occurred during organizing."); // Fixed
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
            string color;
            string icon;
            string levelText;

            switch (level)
            {
                case AppLogLevel.Success:
                    color = "#34D399";
                    icon = "✔";
                    levelText = "SUCCESS";
                    break;

                case AppLogLevel.Error:
                    color = "#F87171";
                    icon = "✖";
                    levelText = "ERROR";
                    break;

                case AppLogLevel.Warning:
                    color = "#FBBF24";
                    icon = "⚠";
                    levelText = "WARN";
                    break;

                case AppLogLevel.Debug:
                    color = "#F59E0B";
                    icon = "🐞";
                    levelText = "DEBUG";
                    break;

                case AppLogLevel.Action:
                    color = "#60A5FA";
                    icon = "🚀";
                    levelText = "ACTION";
                    break;

                default:
                    color = "#94A3B8";
                    icon = "ℹ";
                    levelText = "INFO";
                    break;
            }

            var log = new LogEntry
            {
                Timestamp = $"[{DateTime.Now:HH:mm:ss}]",
                Level = levelText,
                Message = message,
                LevelColor = color,
                Icon = icon
            };

            Application.Current.Dispatcher.Invoke(() =>
            {
                LogMessages.Add(log);
            });
        }
    }
}