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

        partial void OnIsCheckedChanged(bool value)
        {
            // If this node shows a checkbox (series folder) and is a folder,
            // propagate this checked state to all descendants.
            if (ShowCheckbox && IsFolder)
            {
                SetChildrenChecked(this, value);
            }
        }

        private void SetChildrenChecked(PreviewItem node, bool isCheckedValue)
        {
            foreach (var child in node.Children)
            {
                // Set child IsChecked regardless of its own ShowCheckbox,
                // so that the filtering logic sees the correct state.
                child.IsChecked = isCheckedValue;

                // Recurse into grandchildren
                if (child.Children.Count > 0)
                {
                    SetChildrenChecked(child, isCheckedValue);
                }
            }
        }
    }




}
