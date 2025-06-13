using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShowStasher.MVVM.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowStasher.MVVM.ViewModels
{
    public partial class SearchSelectionDialogViewModel : ObservableObject
    {
        public string PromptMessage { get; }

        public ObservableCollection<SearchCandidate> Candidates { get; }

        [ObservableProperty]
        private SearchCandidate? selectedCandidate;

        public IRelayCommand ConfirmCommand { get; }
        public IRelayCommand CancelCommand { get; }

        // TaskCompletionSource to signal the chosen ID (or null)
        private readonly TaskCompletionSource<int?> _tcs = new();

        public Task<int?> SelectionTask => _tcs.Task;

        public SearchSelectionDialogViewModel(string promptMessage, IEnumerable<SearchCandidate> candidates)
        {
            PromptMessage = promptMessage;
            Candidates = new ObservableCollection<SearchCandidate>(candidates);

            ConfirmCommand = new RelayCommand(OnConfirm, CanConfirm);
            CancelCommand = new RelayCommand(OnCancel);

            // Reevaluate CanExecute when SelectedCandidate changes
            this.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SelectedCandidate))
                    ConfirmCommand.NotifyCanExecuteChanged();
            };
        }

        private bool CanConfirm() => SelectedCandidate != null;

        private void OnConfirm()
        {
            if (SelectedCandidate != null)
                _tcs.TrySetResult(SelectedCandidate.Id);
            else
                _tcs.TrySetResult(null);

            // The DialogService will close the window when the task completes.
        }

        private void OnCancel()
        {
            _tcs.TrySetResult(null);
            // DialogService will close window.
        }
    }

}
