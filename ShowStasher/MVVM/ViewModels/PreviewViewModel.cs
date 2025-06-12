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
    public partial class PreviewViewModel : ObservableObject
    {
        public ObservableCollection<PreviewItem> RootItems { get; } = new();
        public Action? RequestClose { get; set; }
      

        private bool _hasResponded;

        public bool HasResponded
        {
            get => _hasResponded;
            set => SetProperty(ref _hasResponded, value);
        }

        [RelayCommand]
        private void Confirm()
        {
            if (HasResponded) return;
            HasResponded = true;
            OnConfirm?.Invoke(GetCheckedFiles(RootItems));
            RequestClose?.Invoke();
        }

        [RelayCommand]
        private void Cancel()
        {
            if (HasResponded) return;
            HasResponded = true;
            OnCancel?.Invoke();
            RequestClose?.Invoke();
        }

        public Action<List<PreviewItem>>? OnConfirm;
        public Action? OnCancel;

        private List<PreviewItem> GetCheckedFiles(IEnumerable<PreviewItem> items)
        {
            var files = new List<PreviewItem>();
            foreach (var item in items)
            {
                if (item.IsFile && item.IsChecked)
                    files.Add(item);
                files.AddRange(GetCheckedFiles(item.Children));
            }
            return files;
        }
    }

}
