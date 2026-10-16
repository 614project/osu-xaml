using OsuXaml.Controls;

namespace OsuXaml
{
    /// <summary>
    /// Converters every XAML file can use by name, e.g. <c>Visibility="{Bind IsBusy, Converter=BoolToVisibility}"</c>.
    /// A method with the same name on the x:Class takes precedence.
    /// </summary>
    public static class Converters
    {
        public static bool Not(bool value) => !value;

        public static Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        public static Visibility InverseBoolToVisibility(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

        public static bool IsNull(object value) => value == null;

        public static bool IsNotNull(object value) => value != null;

        public static Visibility NullToVisibility(object value) => value == null ? Visibility.Collapsed : Visibility.Visible;

        public static Visibility NotNullToVisibility(object value) => value == null ? Visibility.Visible : Visibility.Collapsed;

        public static Visibility ZeroToVisibility(int value) => value == 0 ? Visibility.Visible : Visibility.Collapsed;

        public static Visibility NonZeroToVisibility(int value) => value != 0 ? Visibility.Visible : Visibility.Collapsed;

        public static string ToUpper(string value) => value?.ToUpperInvariant();

        public static string ToLower(string value) => value?.ToLowerInvariant();
    }
}
