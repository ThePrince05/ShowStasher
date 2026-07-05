using ShowStasher.MVVM.Models;
using ShowStasher.MVVM.ViewModels;
using ShowStasher.MVVM.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowStasher.Services
{
    public class MetadataSelectionService : IMetadataSelectionService
    {
        // Cache to store selection results for the duration of the app session
        private readonly Dictionary<string, int?> _seriesSelectionCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int?> _movieSelectionCache = new(StringComparer.OrdinalIgnoreCase);

        public async Task<int?> PromptUserToSelectMovieAsync(string originalTitle, IReadOnlyList<SearchCandidate> candidates)
        {
            if (_movieSelectionCache.TryGetValue(originalTitle, out var cachedId))
            {
                return cachedId;
            }

            var result = await ShowSelectionDialogAsync($"Select a movie for: {originalTitle}", candidates);

            // Cache the choice (even if null/canceled, so it doesn't repeatedly prompt on failure)
            _movieSelectionCache[originalTitle] = result;
            return result;
        }

        public async Task<int?> PromptUserToSelectSeriesAsync(string originalTitle, IReadOnlyList<SearchCandidate> candidates)
        {
            // If we already selected a candidate for this series title in this session, return it
            if (_seriesSelectionCache.TryGetValue(originalTitle, out var cachedId))
            {
                return cachedId;
            }

            var result = await ShowSelectionDialogAsync($"Select a series for: {originalTitle}", candidates);

            // Cache the choice for subsequent seasons/episodes
            _seriesSelectionCache[originalTitle] = result;
            return result;
        }

        // Optional: Method to clear the cache if you want to reset selections between different imports
        public void ClearSessionCache()
        {
            _seriesSelectionCache.Clear();
            _movieSelectionCache.Clear();
        }

        private Task<int?> ShowSelectionDialogAsync(string promptMessage, IReadOnlyList<SearchCandidate> candidates)
        {
            var vm = new SearchSelectionDialogViewModel(promptMessage, candidates);
            var dialog = new SearchSelectionDialog
            {
                DataContext = vm,
                Owner = System.Windows.Application.Current.MainWindow
            };

            vm.SelectionTask.ContinueWith(_ =>
            {
                dialog.Dispatcher.Invoke(() => dialog.Close());
            });

            dialog.ShowDialog();
            return vm.SelectionTask;
        }
    }
}