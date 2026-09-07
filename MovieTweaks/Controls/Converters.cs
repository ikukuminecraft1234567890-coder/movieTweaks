using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MovieTweaks.Models;

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

    public class ShapeTypeConverter : IValueConverter
    {
        public static readonly ShapeTypeConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ShapeType st)
            {
                return st switch
                {
                    ShapeType.Rectangle => "⬛ 四角形",
                    ShapeType.RoundedRectangle => "▢ 角丸四角形",
                    ShapeType.Ellipse => "⚪ 円・楕円",
                    ShapeType.Triangle => "▲ 三角形",
                    ShapeType.Star => "★ 星",
                    ShapeType.Heart => "♥ ハート",
                    ShapeType.Diamond => "◆ ひし形",
                    ShapeType.Arrow => "➔ 矢印",
                    ShapeType.Line => "― 直線",
                    ShapeType.SpeechBubble => "💬 吹き出し",
                    _ => value.ToString() ?? ""
                };
            }
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class StrokeStyleConverter : IValueConverter
    {
        public static readonly StrokeStyleConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is StrokeStyle ss)
            {
                return ss switch
                {
                    StrokeStyle.Solid => "実線 ――",
                    StrokeStyle.Dash => "破線 ┈┈",
                    StrokeStyle.Dot => "点線 ････",
                    _ => value.ToString() ?? ""
                };
            }
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
