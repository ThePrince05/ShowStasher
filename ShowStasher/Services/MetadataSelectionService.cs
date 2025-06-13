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
        public Task<int?> PromptUserToSelectMovieAsync(string originalTitle, IReadOnlyList<SearchCandidate> candidates)
        {
            return ShowSelectionDialogAsync($"Select a movie for: {originalTitle}", candidates);
        }

        public Task<int?> PromptUserToSelectSeriesAsync(string originalTitle, IReadOnlyList<SearchCandidate> candidates)
        {
            return ShowSelectionDialogAsync($"Select a series for: {originalTitle}", candidates);
        }

        private Task<int?> ShowSelectionDialogAsync(string promptMessage, IReadOnlyList<SearchCandidate> candidates)
        {
            var vm = new SearchSelectionDialogViewModel(promptMessage, candidates);
            var dialog = new SearchSelectionDialog
            {
                DataContext = vm,
                Owner = System.Windows.Application.Current.MainWindow
            };

            // Close the dialog when the selection task completes
            vm.SelectionTask.ContinueWith(_ =>
            {
                // Must close on the UI thread
                dialog.Dispatcher.Invoke(() => dialog.Close());
            });

            dialog.ShowDialog(); // blocks until dialog is closed
            return vm.SelectionTask;
        }

    }
}
