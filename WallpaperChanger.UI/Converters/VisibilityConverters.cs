using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WallpaperChanger.UI.Converters;

/// <summary>値が null のときに表示する（プレースホルダー用）。</summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class NullToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>数値が 0 のときに表示する（空リストの案内用）。</summary>
[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
