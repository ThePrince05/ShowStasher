using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;

namespace ShowStasher.MVVM.Models
{
    public class PreviewItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? RenameTemplate { get; set; }
        public DataTemplate? DefaultTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is PreviewItem pi)
            {
                // Only show rename template if it’s a file AND both OriginalName and RenamedName are non-null
                // You can also check if they differ: pi.OriginalName != pi.RenamedName
                if (pi.IsFile
                    && !string.IsNullOrEmpty(pi.OriginalName)
                    && !string.IsNullOrEmpty(pi.RenamedName))
                {
                    return RenameTemplate ?? DefaultTemplate;
                }
            }
            return DefaultTemplate;
        }
    }
}
