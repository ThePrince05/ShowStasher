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

        [RelayCommand]
        private void Confirm()
        {
            var selectedFiles = GetCheckedFiles(RootItems);
            OnConfirm?.Invoke(selectedFiles);
        }

        [RelayCommand]
        private void Cancel()
        {
            OnCancel?.Invoke();
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
