using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace VolejbalVrsovice.App.Helpers
{
    /// <summary>Prázdný text (nebo <c>null</c>) → <see cref="Visibility.Collapsed"/>, jinak viditelné.</summary>
    public sealed class TextToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary><c>true</c> → <see cref="Visibility.Collapsed"/>, <c>false</c> → viditelné.</summary>
    public sealed class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public static class WindowBehaviors
    {
        /// <summary>Po otevření okna přesune fokus na první pole, do kterého lze psát.</summary>
        public static readonly DependencyProperty FocusFirstInputProperty = DependencyProperty.RegisterAttached(
            "FocusFirstInput", typeof(bool), typeof(WindowBehaviors), new PropertyMetadata(false, OnFocusFirstInputChanged));

        public static bool GetFocusFirstInput(DependencyObject element)
        {
            return (bool)element.GetValue(FocusFirstInputProperty);
        }

        public static void SetFocusFirstInput(DependencyObject element, bool value)
        {
            element.SetValue(FocusFirstInputProperty, value);
        }

        private static void OnFocusFirstInputChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
        {
            var window = element as FrameworkElement;
            if (window == null || !(bool)e.NewValue)
            {
                return;
            }

            window.Loaded += (sender, args) =>
                window.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
    }
}
