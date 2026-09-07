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

        public static DoubleCollection? GetStrokeDashArray(StrokeStyle style) => style switch
        {
            StrokeStyle.Dash => new DoubleCollection { 4, 2 },
            StrokeStyle.Dot => new DoubleCollection { 1, 2 },
            _ => null
        };

        private static FrameworkElement CreateShapeVisual(ShapeOverlay item, int width, int height)
        {
            var strokeBrush = TryParseBrush(item.StrokeColor, Brushes.Red);
            var fillBrush = TryParseBrush(item.FillColor, Brushes.Transparent);
            var dashArray = GetStrokeDashArray(item.StrokeStyle);
            double thickness = item.StrokeThickness;
            double w = width;
            double h = height;

            switch (item.ShapeType)
            {
                case ShapeType.Rectangle:
                    return new Rectangle
                    {
                        Width = w,
                        Height = h,
                        Stroke = strokeBrush,
                        Fill = fillBrush,
                        StrokeThickness = thickness,
                        StrokeDashArray = dashArray
                    };

                case ShapeType.RoundedRectangle:
                    return new Rectangle
                    {
                        Width = w,
                        Height = h,
                        Stroke = strokeBrush,
                        Fill = fillBrush,
                        StrokeThickness = thickness,
                        RadiusX = item.CornerRadius,
                        RadiusY = item.CornerRadius,
                        StrokeDashArray = dashArray
                    };

                case ShapeType.Ellipse:
                    return new Ellipse
                    {
                        Width = w,
                        Height = h,
                        Stroke = strokeBrush,
                        Fill = fillBrush,
                        StrokeThickness = thickness,
                        StrokeDashArray = dashArray
                    };

                case ShapeType.Triangle:
                    double tHalf = thickness / 2.0;
                    return new Polygon
                    {
                        Points = new PointCollection
                        {
                            new Point(w / 2.0, tHalf),
                            new Point(w - tHalf, h - tHalf),
                            new Point(tHalf, h - tHalf)
                        },
                        Stroke = strokeBrush,
                        Fill = fillBrush,
                        StrokeThickness = thickness,
                        StrokeDashArray = dashArray
                    };

                case ShapeType.Diamond:
                    double dtHalf = thickness / 2.0;
                    return new Polygon
                    {
                        Points = new PointCollection
                        {
                            new Point(w / 2.0, dtHalf),
                            new Point(w - dtHalf, h / 2.0),
                            new Point(w / 2.0, h - dtHalf),
                            new Point(dtHalf, h / 2.0)
                        },
                        Stroke = strokeBrush,
                        Fill = fillBrush,
                        StrokeThickness = thickness,
                        StrokeDashArray = dashArray
                    };

                case ShapeType.Star:
                    double cx = w / 2.0, cy = h / 2.0;
                    double rx = Math.Max(1, (w - thickness) / 2.0);
                    double ry = Math.Max(1, (h - thickness) / 2.0);
                    var starPoints = new PointCollection();
                    for (int i = 0; i < 10; i++)
                    {
                        double angle = -Math.PI / 2.0 + i * Math.PI / 5.0;
                        double rRatio = (i % 2 == 0) ? 1.0 : 0.45;
                        starPoints.Add(new Point(cx + rx * rRatio * Math.Cos(angle), cy + ry * rRatio * Math.Sin(angle)));
                    }
                    return new Polygon
                    {
                        Points = starPoints,
                        Stroke = strokeBrush,
                        Fill = fillBrush,
                        StrokeThickness = thickness,
                        StrokeDashArray = dashArray
                    };

                case ShapeType.Heart:
                    var heartGeom = new PathGeometry();
                    double ht = thickness / 2.0;
                    double pw = Math.Max(1, w - thickness);
                    double ph = Math.Max(1, h - thickness);
                    var heartFig = new PathFigure
                    {
                        StartPoint = new Point(ht + pw * 0.5, ht + ph * 0.25),
                        IsClosed = true,
                        IsFilled = true
                    };
                    heartFig.Segments.Add(new BezierSegment(
                        new Point(ht + pw * 0.5, ht),
                        new Point(ht, ht),
                        new Point(ht, ht + ph * 0.4), true));
                    heartFig.Segments.Add(new BezierSegment(
                        new Point(ht, ht + ph * 0.7),
                        new Point(ht + pw * 0.35, ht + ph * 0.85),
                        new Point(ht + pw * 0.5, ht + ph), true));
                    heartFig.Segments.Add(new BezierSegment(
                        new Point(ht + pw * 0.65, ht + ph * 0.85),
                        new Point(ht + pw, ht + ph * 0.7),
                        new Point(ht + pw, ht + ph * 0.4), true));
                    heartFig.Segments.Add(new BezierSegment(
                        new Point(ht + pw, ht),
                        new Point(ht + pw * 0.5, ht),
                        new Point(ht + pw * 0.5, ht + ph * 0.25), true));
                    heartGeom.Figures.Add(heartFig);
                    return new System.Windows.Shapes.Path
                    {
                        Data = heartGeom,
                        Stroke = strokeBrush,
                        Fill = fillBrush,
                        StrokeThickness = thickness,
                        StrokeDashArray = dashArray
                    };

                case ShapeType.SpeechBubble:
                    var bubbleGeom = new PathGeometry();
                    double bt = thickness / 2.0;
                    double bpw = Math.Max(1, w - thickness);
                    double tailH = Math.Min(h * 0.25, 25.0);
                    double bodyH = Math.Max(1, h - thickness - tailH);
                    double cr = Math.Min(item.CornerRadius, Math.Min(bpw / 2.0, bodyH / 2.0));
                    var bubbleFig = new PathFigure
                    {
                        StartPoint = new Point(bt + cr, bt),
                        IsClosed = true,
                        IsFilled = true
                    };
                    bubbleFig.Segments.Add(new LineSegment(new Point(bt + bpw - cr, bt), true));
                    bubbleFig.Segments.Add(new ArcSegment(new Point(bt + bpw, bt + cr), new Size(cr, cr), 0, false, SweepDirection.Clockwise, true));
                    bubbleFig.Segments.Add(new LineSegment(new Point(bt + bpw, bt + bodyH - cr), true));
                    bubbleFig.Segments.Add(new ArcSegment(new Point(bt + bpw - cr, bt + bodyH), new Size(cr, cr), 0, false, SweepDirection.Clockwise, true));
                    bubbleFig.Segments.Add(new LineSegment(new Point(bt + bpw * 0.4, bt + bodyH), true));
                    bubbleFig.Segments.Add(new LineSegment(new Point(bt + bpw * 0.2, bt + h - thickness), true));
                    bubbleFig.Segments.Add(new LineSegment(new Point(bt + bpw * 0.25, bt + bodyH), true));
                    bubbleFig.Segments.Add(new LineSegment(new Point(bt + cr, bt + bodyH), true));
                    bubbleFig.Segments.Add(new ArcSegment(new Point(bt, bt + bodyH - cr), new Size(cr, cr), 0, false, SweepDirection.Clockwise, true));
                    bubbleFig.Segments.Add(new LineSegment(new Point(bt, bt + cr), true));
                    bubbleFig.Segments.Add(new ArcSegment(new Point(bt + cr, bt), new Size(cr, cr), 0, false, SweepDirection.Clockwise, true));
                    bubbleGeom.Figures.Add(bubbleFig);
                    return new System.Windows.Shapes.Path
                    {
                        Data = bubbleGeom,
                        Stroke = strokeBrush,
                        Fill = fillBrush,
                        StrokeThickness = thickness,
                        StrokeDashArray = dashArray
                    };

                case ShapeType.Arrow:
                    var arrowCanvas = new Canvas { Width = w, Height = h };
                    var line = new Line
                    {
                        X1 = thickness,
                        Y1 = h / 2.0,
                        X2 = w - 10,
                        Y2 = h / 2.0,
                        Stroke = strokeBrush,
                        StrokeThickness = thickness,
                        StrokeDashArray = dashArray
                    };
                    var headSize = Math.Max(12, thickness * 3);
                    var arrowHead = new Polygon
                    {
                        Fill = strokeBrush,
                        Points = new PointCollection
                        {
                            new Point(w, h / 2.0),
                            new Point(w - headSize, h / 2.0 - headSize * 0.6),
                            new Point(w - headSize, h / 2.0 + headSize * 0.6)
                        }
                    };
                    arrowCanvas.Children.Add(line);
                    arrowCanvas.Children.Add(arrowHead);
                    return arrowCanvas;

                case ShapeType.Line:
                default:
                    return new Line
                    {
                        X1 = thickness,
                        Y1 = h / 2.0,
                        X2 = w - thickness,
                        Y2 = h / 2.0,
                        Stroke = strokeBrush,
                        StrokeThickness = thickness,
                        StrokeDashArray = dashArray
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
