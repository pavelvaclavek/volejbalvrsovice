using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace VolejbalVrsovice.App.Helpers
{
    /// <summary>
    /// Obousměrná vazba hesla z <see cref="PasswordBox"/> na ViewModel. <c>PasswordBox.Password</c>
    /// není dependency property, takže se na něj napřímo vázat nedá.
    /// <code>&lt;PasswordBox h:PasswordBoxBinding.Password="{Binding Password}" /&gt;</code>
    /// </summary>
    public static class PasswordBoxBinding
    {
        public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
            "Password",
            typeof(string),
            typeof(PasswordBoxBinding),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordPropertyChanged));

        private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
            "IsUpdating", typeof(bool), typeof(PasswordBoxBinding), new PropertyMetadata(false));

        static PasswordBoxBinding()
        {
            // Třídní handler místo přihlašování k jednotlivým instancím: funguje i tehdy, když je
            // výchozí hodnota vazby stejná jako výchozí hodnota vlastnosti (prázdný řetězec).
            EventManager.RegisterClassHandler(
                typeof(PasswordBox), PasswordBox.PasswordChangedEvent, new RoutedEventHandler(OnPasswordChanged));
        }

        public static string GetPassword(DependencyObject element)
        {
            return (string)element.GetValue(PasswordProperty);
        }

        public static void SetPassword(DependencyObject element, string value)
        {
            element.SetValue(PasswordProperty, value);
        }

        // ViewModel → PasswordBox (např. vymazání pole po odeslání).
        private static void OnPasswordPropertyChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
        {
            var box = element as PasswordBox;
            if (box == null || (bool)box.GetValue(IsUpdatingProperty))
            {
                return;
            }

            var password = (string)e.NewValue ?? string.Empty;
            if (box.Password != password)
            {
                box.Password = password;
            }
        }

        // PasswordBox → ViewModel (uživatel píše).
        private static void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            var box = (PasswordBox)sender;
            if (BindingOperations.GetBindingExpression(box, PasswordProperty) == null)
            {
                return;
            }

            box.SetValue(IsUpdatingProperty, true);
            try
            {
                box.SetCurrentValue(PasswordProperty, box.Password);
            }
            finally
            {
                box.SetValue(IsUpdatingProperty, false);
            }
        }
    }
}
