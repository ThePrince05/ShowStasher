using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShowStasher.Helpers
{
    public static class TextBlockExtensions
    {
        public static readonly DependencyProperty AutoToolTipIfTrimmedProperty =
            DependencyProperty.RegisterAttached(
                "AutoToolTipIfTrimmed",
                typeof(bool),
                typeof(TextBlockExtensions),
                new PropertyMetadata(false, OnAutoToolTipIfTrimmedChanged));

        public static bool GetAutoToolTipIfTrimmed(DependencyObject obj) => (bool)obj.GetValue(AutoToolTipIfTrimmedProperty);
        public static void SetAutoToolTipIfTrimmed(DependencyObject obj, bool value) => obj.SetValue(AutoToolTipIfTrimmedProperty, value);

        private static void OnAutoToolTipIfTrimmedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock textBlock && (bool)e.NewValue)
            {
                textBlock.SizeChanged += TextBlock_SizeChanged;
            }
        }

        private static void TextBlock_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is TextBlock textBlock)
            {
                // Compare ActualHeight to a freshly formatted text bounding box
                var typeface = new Typeface(
                    textBlock.FontFamily,
                    textBlock.FontStyle,
                    textBlock.FontWeight,
                    textBlock.FontStretch);

                var formattedText = new FormattedText(
                    textBlock.Text ?? string.Empty,
                    System.Globalization.CultureInfo.CurrentCulture,
                    textBlock.FlowDirection,
                    typeface,
                    textBlock.FontSize,
                    textBlock.Foreground,
                    VisualTreeHelper.GetDpi(textBlock).PixelsPerDip)
                {
                    // Default to 120 (from your XAML StackPanel) if ActualWidth hasn't rendered yet
                    MaxTextWidth = textBlock.ActualWidth > 0 ? textBlock.ActualWidth : 120,
                    MaxTextHeight = double.PositiveInfinity
                };

                // If the text actually wants more height than the designated ActualHeight (or 40px), it's trimmed
                bool isTrimmed = formattedText.Height > textBlock.ActualHeight;

                // Assign or clear the tooltip
                textBlock.ToolTip = isTrimmed ? textBlock.Text : null;
            }
        }
    }
}