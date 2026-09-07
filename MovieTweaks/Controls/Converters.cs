using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MovieTweaks.Controls
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public static readonly BooleanToVisibilityConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return (value is true) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is Visibility.Visible;
        }
    }

    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public static readonly InverseBooleanToVisibilityConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return (value is true) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is not Visibility.Visible;
        }
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public static readonly NullToVisibilityConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // If null, visible (showing placeholder). If not null, collapsed.
            return value == null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PercentConverter : IValueConverter
    {
        public static readonly PercentConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double d)
            {
                return $"{Math.Round(d, 1):0.#}%";
            }
            if (value is int i)
            {
                return $"{i}%";
            }
            return "100%";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string str)
            {
                str = str.Replace("%", "").Trim();
                if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ||
                    double.TryParse(str, NumberStyles.Any, CultureInfo.CurrentCulture, out parsed))
                {
                    return parsed;
                }
            }
            return Binding.DoNothing;
        }
    }

    public class UnitConverter : IValueConverter
    {
        public static readonly UnitConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string unit = parameter as string ?? "";
            if (value is double d)
            {
                return $"{Math.Round(d, 1):0.#} {unit}".Trim();
            }
            if (value is int i)
            {
                return $"{i} {unit}".Trim();
            }
            return $"{value} {unit}".Trim();
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string str)
            {
                string unit = parameter as string ?? "";
                if (!string.IsNullOrEmpty(unit)) str = str.Replace(unit, "");
                str = str.Trim();
                if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ||
                    double.TryParse(str, NumberStyles.Any, CultureInfo.CurrentCulture, out parsed))
                {
                    return parsed;
                }
            }
            return Binding.DoNothing;
        }
    }
}
