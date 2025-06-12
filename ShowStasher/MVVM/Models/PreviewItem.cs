using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShowStasher.MVVM.Models
{
    public partial class PreviewItem
    {
        public string Name { get; set; } = "";
        public string? OriginalName { get; set; }  
        public string? RenamedName { get; set; }   
        public string SourcePath { get; set; } = "";
        public bool IsFolder { get; set; } 
        public string DestinationPath { get; set; } = ""; 
        public bool IsFile { get; set; }
        public bool IsChecked { get; set; } = true;
        public ObservableCollection<PreviewItem> Children { get; set; } = new();
    }


}
