using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MovieTweaks.Models;

namespace MovieTweaks.Services
{
    public static class OverlayRenderer
    {
        public static string RenderOverlayToTempPng(OverlayItem item)
        {
            var width = Math.Max(1, (int)Math.Round(item.Width));
            var height = Math.Max(1, (int)Math.Round(item.Height));

            var container = new Grid
            {
                Width = width,
                Height = height,
                Background = Brushes.Transparent
            };

            FrameworkElement element = item switch
            {
                TextOverlay textItem => CreateTextVisual(textItem, width, height),
                ShapeOverlay shapeItem => CreateShapeVisual(shapeItem, width, height),
                ImageOverlay imgItem => CreateImageVisual(imgItem, width, height),
                _ => new Border()
            };

            container.Children.Add(element);
            container.Opacity = item.Opacity;

            if (Math.Abs(item.Rotation) > 0.01)
            {
                container.RenderTransform = new RotateTransform(item.Rotation);
                container.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            container.Measure(new Size(width, height));
            container.Arrange(new Rect(0, 0, width, height));
            container.UpdateLayout();

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(container);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ov_{item.Id}_{Guid.NewGuid():N}.png");
            using (var stream = File.OpenWrite(tempPath))
            {
                encoder.Save(stream);
            }

            return tempPath;
        }

        private static FrameworkElement CreateTextVisual(TextOverlay item, int width, int height)
        {
            var border = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(item.BackgroundCornerRadius),
                Background = TryParseBrush(item.BackgroundColor, Brushes.Transparent),
                Padding = new Thickness(6)
            };

            // Custom outlined text or simple text block
            var tb = new TextBlock
            {
                Text = item.Text,
                FontFamily = new FontFamily(item.FontFamily),
                FontSize = item.FontSize,
                Foreground = TryParseBrush(item.TextColor, Brushes.White),
                FontWeight = item.IsBold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = item.IsItalic ? FontStyles.Italic : FontStyles.Normal,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = item.TextAlignment switch
                {
                    "Left" => HorizontalAlignment.Left,
                    "Right" => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Center
                },
                VerticalAlignment = VerticalAlignment.Center
            };

            // Add drop shadow / outline effect if outline thickness > 0
            if (item.OutlineThickness > 0 && !string.IsNullOrEmpty(item.OutlineColor))
            {
                var outlineColor = TryParseColor(item.OutlineColor, Colors.Black);
                tb.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = outlineColor,
                    BlurRadius = item.OutlineThickness * 2,
                    ShadowDepth = 0,
                    Opacity = 1.0
                };
            }

            border.Child = tb;
            return border;
        }

        private static FrameworkElement CreateShapeVisual(ShapeOverlay item, int width, int height)
        {
            var strokeBrush = TryParseBrush(item.StrokeColor, Brushes.Red);
            var fillBrush = TryParseBrush(item.FillColor, Brushes.Transparent);

            switch (item.ShapeType)
            {
                case ShapeType.Rectangle:
                    return new Rectangle
                    {
                        Width = width,
                        Height = height,
                        Stroke = strokeBrush,
                        Fill = fillBrush,
                        StrokeThickness = item.StrokeThickness,
                        RadiusX = item.CornerRadius,
                        RadiusY = item.CornerRadius
                    };

                case ShapeType.Ellipse:
                    return new Ellipse
                    {
                        Width = width,
                        Height = height,
                        Stroke = strokeBrush,
                        Fill = fillBrush,
                        StrokeThickness = item.StrokeThickness
                    };

                case ShapeType.Arrow:
                    var arrowCanvas = new Canvas { Width = width, Height = height };
                    // Draw horizontal arrow from left to right
                    var line = new Line
                    {
                        X1 = item.StrokeThickness,
                        Y1 = height / 2.0,
                        X2 = width - 10,
                        Y2 = height / 2.0,
                        Stroke = strokeBrush,
                        StrokeThickness = item.StrokeThickness
                    };
                    var headSize = Math.Max(12, item.StrokeThickness * 3);
                    var arrowHead = new Polygon
                    {
                        Fill = strokeBrush,
                        Points = new PointCollection
                        {
                            new Point(width, height / 2.0),
                            new Point(width - headSize, height / 2.0 - headSize * 0.6),
                            new Point(width - headSize, height / 2.0 + headSize * 0.6)
                        }
                    };
                    arrowCanvas.Children.Add(line);
                    arrowCanvas.Children.Add(arrowHead);
                    return arrowCanvas;

                case ShapeType.Line:
                default:
                    return new Line
                    {
                        X1 = item.StrokeThickness,
                        Y1 = height / 2.0,
                        X2 = width - item.StrokeThickness,
                        Y2 = height / 2.0,
                        Stroke = strokeBrush,
                        StrokeThickness = item.StrokeThickness
                    };
            }
        }

        private static FrameworkElement CreateImageVisual(ImageOverlay item, int width, int height)
        {
            if (File.Exists(item.ImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(item.ImagePath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();

                    return new Image
                    {
                        Source = bmp,
                        Width = width,
                        Height = height,
                        Stretch = item.KeepAspectRatio ? Stretch.Uniform : Stretch.Fill
                    };
                }
                catch { }
            }

            return new Border { Width = width, Height = height, Background = Brushes.Gray };
        }

        public static Brush TryParseBrush(string hexOrName, Brush fallback)
        {
            try
            {
                if (string.Equals(hexOrName, "Transparent", StringComparison.OrdinalIgnoreCase))
                    return Brushes.Transparent;
                var brush = new BrushConverter().ConvertFromString(hexOrName) as Brush;
                return brush ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        public static Color TryParseColor(string hexOrName, Color fallback)
        {
            try
            {
                var c = ColorConverter.ConvertFromString(hexOrName);
                if (c is Color col) return col;
                return fallback;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
