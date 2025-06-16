using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using System.Threading.Tasks;
using ShowStasher.Services;
using ShowStasher.MVVM.Models;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace ShowStasher.MVVM.ViewModels
{
    public partial class HistoryViewModel : ObservableObject
    {
        private readonly SqliteDbService _dbService;

        public ObservableCollection<MoveHistory> MoveHistories { get; } = new();

        [ObservableProperty]
        private string searchText = "";

        public ICollectionView CollectionView { get; }

        public HistoryViewModel(SqliteDbService dbService)
        {
            _dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));

            CollectionView = CollectionViewSource.GetDefaultView(MoveHistories);
            CollectionView.Filter = FilterHistory;

            // Auto-refresh filtering when SearchText changes
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SearchText))
                    CollectionView.Refresh();
            };
        }

        private bool FilterHistory(object obj)
        {
            if (obj is not MoveHistory history) return false;
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            var search = SearchText.ToLowerInvariant();
            return (history.OriginalFileName?.ToLowerInvariant().Contains(search) ?? false)
                || (history.NewFileName?.ToLowerInvariant().Contains(search) ?? false)
                || (history.SourcePath?.ToLowerInvariant().Contains(search) ?? false)
                || (history.DestinationPath?.ToLowerInvariant().Contains(search) ?? false);
        }

        public async Task LoadAsync()
        {
            MoveHistories.Clear();
            var allItems = await _dbService.GetAllMoveHistoryAsync();
            foreach (var item in allItems)
                MoveHistories.Add(item);
        }

        [RelayCommand]
        private async Task Refresh()
        {
            await LoadAsync();
        }

        [RelayCommand]
        private async Task Delete(MoveHistory item)
        {
            if (item != null)
            {
                await _dbService.DeleteMoveHistoryAsync(item.Id);
                MoveHistories.Remove(item);
            }
        }

        [RelayCommand]
        private async Task ClearAll()
        {
            await _dbService.ClearAllHistoryAsync();
            MoveHistories.Clear();
        }
        [RelayCommand]
        private void OpenDestination(MoveHistory item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.DestinationPath))
                return;

            try
            {
                if (Directory.Exists(item.DestinationPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = item.DestinationPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    // Optional: log or alert user
                    Debug.WriteLine("Destination folder does not exist.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening folder: {ex.Message}");
            }
        }

    }



}
