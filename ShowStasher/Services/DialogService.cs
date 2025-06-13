using ShowStasher.MVVM.Models;
using ShowStasher.MVVM.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;

namespace ShowStasher.Services
{
    public class DialogService : IMetadataSelectionService
    {
        private readonly Window _ownerWindow;

        public DialogService(Window ownerWindow)
        {
            _ownerWindow = ownerWindow;
        }

        public Task<int?> PromptUserToSelectMovieAsync(string originalTitle, IReadOnlyList<SearchCandidate> candidates)
        {
            return ShowSelectionDialogAsync(originalTitle, candidates, "Select Movie Match");
        }

        public Task<int?> PromptUserToSelectSeriesAsync(string originalTitle, IReadOnlyList<SearchCandidate> candidates)
        {
            return ShowSelectionDialogAsync(originalTitle, candidates, "Select Series Match");
        }

        private Task<int?> ShowSelectionDialogAsync(string originalTitle, IReadOnlyList<SearchCandidate> candidates, string windowTitle)
        {
            var tcs = new TaskCompletionSource<int?>();
            // Must open on UI thread:
            Application.Current.Dispatcher.Invoke(() =>
            {
                var vm = new SearchSelectionDialogViewModel($"Multiple matches found for '{originalTitle}'. Please select:", candidates);
                var window = new ShowStasher.MVVM.Views.SearchSelectionDialog
                {
                    DataContext = vm,
                    Owner = _ownerWindow,
                    Title = windowTitle
                };

                // When user makes a choice (vm.SelectionTask completes), close the window:
                vm.SelectionTask.ContinueWith(t =>
                {
                    // Close on UI thread
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // Optionally: window.DialogResult = true;
                        window.Close();
                    });
                }, TaskScheduler.Default);

                // ShowDialog is blocking until window.Close() is called
                window.ShowDialog();

                // After ShowDialog returns, vm.SelectionTask is already completed
                tcs.SetResult(vm.SelectionTask.Result);
            });
            return tcs.Task;
        }
    }
}
