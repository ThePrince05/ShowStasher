using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized; // Added for handling collection updates
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;

namespace ShowStasher.Helpers
{
    public static class ListBoxBehaviors
    {
        // ==========================================
        // 1. Existing Behavior: BindableSelectedItems
        // ==========================================
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

        // ==========================================
        // 2. New Behavior: AutoScrollToBottom
        // ==========================================
        public static readonly DependencyProperty AutoScrollToBottomProperty =
            DependencyProperty.RegisterAttached(
                "AutoScrollToBottom",
                typeof(bool),
                typeof(ListBoxBehaviors),
                new PropertyMetadata(false, OnAutoScrollToBottomChanged));

        public static bool GetAutoScrollToBottom(DependencyObject obj)
        {
            return (bool)obj.GetValue(AutoScrollToBottomProperty);
        }

        public static void SetAutoScrollToBottom(DependencyObject obj, bool value)
        {
            obj.SetValue(AutoScrollToBottomProperty, value);
        }

        private static void OnAutoScrollToBottomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is System.Windows.Controls.ListBox listBox)
            {
                if (listBox.Items is INotifyCollectionChanged notifyCollection)
                {
                    // Clean up potential old subscription if reassigned
                    notifyCollection.CollectionChanged -= LogCollectionChanged;

                    if ((bool)e.NewValue)
                    {
                        notifyCollection.CollectionChanged += LogCollectionChanged;
                    }

                    // Local closure function handles the collection addition
                    void LogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
                    {
                        if (args.Action == NotifyCollectionChangedAction.Add)
                        {
                            // Dispatcher schedules scrolling right after WPF renders the new UI row item
                            listBox.Dispatcher.BeginInvoke(() =>
                            {
                                if (listBox.Items.Count > 0)
                                {
                                    var lastItem = listBox.Items[listBox.Items.Count - 1];
                                    listBox.ScrollIntoView(lastItem);
                                }
                            });
                        }
                    }
                }
            }
        }
    }
}