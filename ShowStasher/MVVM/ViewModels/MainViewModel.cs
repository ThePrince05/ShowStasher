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

        public ObservableCollection<string> SelectedLogMessages { get; } = new();

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

            var selectionService = new MetadataSelectionService();

            TMDbService tmdbService = null;

            if (string.IsNullOrWhiteSpace(tmdbApiKey))
            {
                Log("ERROR: TMDb API key is missing in app.config");
                tmdbService = null; // or a fallback instance if you implement one
            }
            else
            {
                tmdbService = new TMDbService(tmdbApiKey, cacheService, Log, selectionService);
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
            if (SelectedLogMessages.Any())
            {
                var combined = string.Join(Environment.NewLine, SelectedLogMessages);
                Clipboard.SetText(combined);
                StatusMessage = "Copied selected logs to clipboard.";
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

        private bool CanCopySelectedLog() => SelectedLogMessages.Any();




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
                Log("Missing source or destination path.");
                return;
            }

            StatusMessage = "Preparing preview...";
            Log("Generating dry run preview...");
            IsBusy = true;
            Progress = 0;

            try
            {
                var previewItems = await _fileOrganizerService.GetDryRunTreeAsync(SourcePath, IsOfflineMode);

                var previewViewModel = new PreviewViewModel(Log);
                // Fill preview items
                previewViewModel.RootItems.Clear();
                foreach (var item in previewItems)
                {
                    previewViewModel.RootItems.Add(item);
                }

                // Create dialog first
                var dialog = new PreviewDialog
                {
                    DataContext = previewViewModel,
                    Owner = Application.Current.MainWindow
                };

                // Hook RequestClose after dialog exists
                previewViewModel.RequestClose = () => dialog.Close();

                // Use TrySetResult on tcs:
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

                // Show the preview dialog
                dialog.ShowDialog();

                var selectedFilesToOrganize = await tcs.Task;

                if (selectedFilesToOrganize.Count == 0)
                {
                    StatusMessage = "Organization canceled.";
                    Log("User canceled file organization.");
                    return;
                }

                StatusMessage = "Organizing selected files...";
                Log("Started organizing selected files...");

                var progressReporter = new Progress<int>(percent => Progress = percent);

                await _fileOrganizerService.OrganizeFilesAsync(
                    selectedFilesToOrganize, DestinationPath, IsOfflineMode, progressReporter);

                StatusMessage = "Done!";
                Log("Finished organizing selected files.");
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

        [RelayCommand]
        private void OpenHistory()
        {
            // Open the History window
            //  new HistoryWindow().ShowDialog();
            System.Windows.MessageBox.Show("Open History Window");
        }

        [RelayCommand]
        private void OpenApiKeyDialog()
        {
            // Open the API key dialog
            // new ApiKeyDialog().ShowDialog();
            System.Windows.MessageBox.Show("Open ApiKey Window");
        }
        private List<PreviewItem> GetCheckedFiles(IEnumerable<PreviewItem> items)
        {
            var files = new List<PreviewItem>();
            var validMetadataFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Traverse(IEnumerable<PreviewItem> children)
            {
                foreach (var item in children)
                {
                    if (item.IsFile)
                    {
                        // Metadata detection: simple filename check
                        var fileName = Path.GetFileName(item.DestinationPath);
                        bool isMetadata = fileName.Equals("poster.jpg", StringComparison.OrdinalIgnoreCase)
                                       || fileName.Equals("synopsis.txt", StringComparison.OrdinalIgnoreCase);

                        if (item.IsChecked && !isMetadata)
                        {
                            files.Add(item);

                            // Track the folder where this media file resides
                            var parentFolder = Path.GetDirectoryName(item.DestinationPath);
                            if (parentFolder != null)
                                validMetadataFolders.Add(parentFolder);
                        }
                        else if (isMetadata)
                        {
                            var parentFolder = Path.GetDirectoryName(item.DestinationPath);
                            if (parentFolder != null && validMetadataFolders.Contains(parentFolder))
                            {
                                files.Add(item);
                            }
                        }
                    }

                    // Recurse
                    if (item.Children.Count > 0)
                        Traverse(item.Children);
                }
            }

            Traverse(items);
            return files;
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
