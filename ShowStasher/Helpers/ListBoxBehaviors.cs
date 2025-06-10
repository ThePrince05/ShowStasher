using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;

namespace ShowStasher.Helpers
{
    public static class ListBoxBehaviors
    {
        public static readonly DependencyProperty BindableSelectedItemsProperty =
            DependencyProperty.RegisterAttached(
                "BindableSelectedItems",
                typeof(IList),
                typeof(ListBoxBehaviors),
                new PropertyMetadata(null, OnBindableSelectedItemsChanged));

        public static IList GetBindableSelectedItems(DependencyObject obj)
        {
            return (IList)obj.GetValue(BindableSelectedItemsProperty);
        }

        public static void SetBindableSelectedItems(DependencyObject obj, IList value)
        {
            obj.SetValue(BindableSelectedItemsProperty, value);
        }

        private static void OnBindableSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is System.Windows.Controls.ListBox listBox)
            {
                listBox.SelectionChanged -= ListBox_SelectionChanged;
                listBox.SelectionChanged += ListBox_SelectionChanged;
            }
        }

        private static void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBox listBox)
            {
                var boundList = GetBindableSelectedItems(listBox);
                if (boundList == null) return;

                boundList.Clear();
                foreach (var item in listBox.SelectedItems)
                    boundList.Add(item);
            }
        }
    }

}
