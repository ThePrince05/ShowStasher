using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowStasher.MVVM.Models
{
    using CommunityToolkit.Mvvm.ComponentModel;

    public partial class PreviewItem : ObservableObject
    {
        [ObservableProperty]
        private string name = "";

        [ObservableProperty]
        private string? originalName;

        [ObservableProperty]
        private string? renamedName;

        [ObservableProperty]
        private string sourcePath = "";

        [ObservableProperty]
        private bool isFolder;

        [ObservableProperty]
        private string destinationPath = "";

        [ObservableProperty]
        private bool isFile;

        [ObservableProperty]
        private bool isChecked = true;

        [ObservableProperty]
        private bool showCheckbox;

        public ObservableCollection<PreviewItem> Children { get; set; } = new();
    }



}
