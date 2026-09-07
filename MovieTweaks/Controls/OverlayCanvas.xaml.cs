using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MovieTweaks.Models;
using MovieTweaks.Services;

namespace MovieTweaks.Controls
{
    public partial class OverlayCanvas : UserControl
    {
        public static readonly DependencyProperty OverlaysProperty =
            DependencyProperty.Register(nameof(Overlays), typeof(System.Collections.IEnumerable), typeof(OverlayCanvas),
                new PropertyMetadata(null, OnOverlaysChanged));

        public static readonly DependencyProperty CurrentTimeProperty =
            DependencyProperty.Register(nameof(CurrentTime), typeof(double), typeof(OverlayCanvas),
                new PropertyMetadata(0.0, OnCurrentTimeChanged));

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(OverlayCanvas),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

        public static readonly DependencyProperty VideoWidthProperty =
            DependencyProperty.Register(nameof(VideoWidth), typeof(int), typeof(OverlayCanvas),
                new PropertyMetadata(1920, OnLayoutParameterChanged));

        public static readonly DependencyProperty VideoHeightProperty =
            DependencyProperty.Register(nameof(VideoHeight), typeof(int), typeof(OverlayCanvas),
                new PropertyMetadata(1080, OnLayoutParameterChanged));

        public event EventHandler? CanvasEmptyClicked;

        public System.Collections.IEnumerable? Overlays
        {
            get => (System.Collections.IEnumerable?)GetValue(OverlaysProperty);
            set => SetValue(OverlaysProperty, value);
        }

        public double CurrentTime
        {
            get => (double)GetValue(CurrentTimeProperty);
            set => SetValue(CurrentTimeProperty, value);
        }

        public object? SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public int VideoWidth
        {
            get => (int)GetValue(VideoWidthProperty);
            set => SetValue(VideoWidthProperty, value);
        }

        public int VideoHeight
        {
            get => (int)GetValue(VideoHeightProperty);
            set => SetValue(VideoHeightProperty, value);
        }

        // Interaction state
        private enum DragMode { None, Move, ResizeNW, ResizeN, ResizeNE, ResizeE, ResizeSE, ResizeS, ResizeSW, ResizeW }
        private DragMode _dragMode = DragMode.None;
        private Point _dragStartMousePos;
        private Rect _dragStartItemRect;
        private const double HandleSize = 10.0;

        public OverlayCanvas()
        {
            InitializeComponent();
            SizeChanged += (s, e) => InvalidateVisuals();
            Loaded += (s, e) => InvalidateVisuals();
        }

        private static void OnOverlaysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is OverlayCanvas canvas)
            {
                if (e.OldValue is INotifyCollectionChanged oldList)
                    oldList.CollectionChanged -= canvas.OnCollectionChanged;
                if (e.NewValue is INotifyCollectionChanged newList)
                    newList.CollectionChanged += canvas.OnCollectionChanged;

                canvas.InvalidateVisuals();
            }
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (OverlayItem item in e.NewItems)
                    item.PropertyChanged += OnItemPropertyChanged;
            }
            if (e.OldItems != null)
            {
                foreach (OverlayItem item in e.OldItems)
                    item.PropertyChanged -= OnItemPropertyChanged;
            }
            InvalidateVisuals();
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            InvalidateVisuals();
        }

        private static void OnCurrentTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is OverlayCanvas canvas) canvas.InvalidateVisuals();
        }

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is OverlayCanvas canvas) canvas.InvalidateVisuals();
        }

        private static void OnLayoutParameterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is OverlayCanvas canvas) canvas.InvalidateVisuals();
        }

        private double ScaleX => ActualWidth > 0 && VideoWidth > 0 ? ActualWidth / VideoWidth : 1.0;
        private double ScaleY => ActualHeight > 0 && VideoHeight > 0 ? ActualHeight / VideoHeight : 1.0;

        public void InvalidateVisuals()
        {
            RootCanvas.Children.Clear();
            if (Overlays == null || ActualWidth <= 0 || ActualHeight <= 0) return;

            var sx = ScaleX;
            var sy = ScaleY;

            foreach (var obj in Overlays)
            {
                if (obj is not OverlayItem item) continue;
                if (!item.IsVisibleAt(CurrentTime)) continue;

                var elem = BuildVisualElement(item, sx, sy);
                Canvas.SetLeft(elem, item.X * sx);
                Canvas.SetTop(elem, item.Y * sy);
                RootCanvas.Children.Add(elem);

                // If selected, add selection adorner frame & handles
                if (item == SelectedItem)
                {
                    BuildSelectionAdorner(item, sx, sy);
                }
            }
        }

        private FrameworkElement BuildVisualElement(OverlayItem item, double sx, double sy)
        {
            var w = Math.Max(5, item.Width * sx);
            var h = Math.Max(5, item.Height * sy);

            var border = new Border
            {
                Width = w,
                Height = h,
                Opacity = item.Opacity,
                Background = Brushes.Transparent,
                Tag = item,
                Cursor = Cursors.SizeAll
            };

            FrameworkElement child = item switch
            {
                TextOverlay t => BuildTextElement(t, w, h, sx, sy),
                ShapeOverlay s => BuildShapeElement(s, w, h, sx, sy),
                ImageOverlay i => BuildImageElement(i, w, h),
                _ => new Border()
            };

            border.Child = child;
            border.MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    SelectedItem = item;
                    _dragMode = DragMode.Move;
                    _dragStartMousePos = e.GetPosition(RootCanvas);
                    _dragStartItemRect = new Rect(item.X, item.Y, item.Width, item.Height);
                    RootCanvas.CaptureMouse();
                    e.Handled = true;
                }
            };

            return border;
        }

        private FrameworkElement BuildTextElement(TextOverlay item, double w, double h, double sx, double sy)
        {
            var border = new Border
            {
                Width = w,
                Height = h,
                CornerRadius = new CornerRadius(item.BackgroundCornerRadius * sx),
                Background = OverlayRenderer.TryParseBrush(item.BackgroundColor, Brushes.Transparent),
                Padding = new Thickness(4 * sx)
            };

            var tb = new TextBlock
            {
                Text = item.Text,
                FontFamily = new FontFamily(item.FontFamily),
                FontSize = Math.Max(8, item.FontSize * sy),
                Foreground = OverlayRenderer.TryParseBrush(item.TextColor, Brushes.White),
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

            if (item.OutlineThickness > 0 && !string.IsNullOrEmpty(item.OutlineColor))
            {
                tb.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = OverlayRenderer.TryParseColor(item.OutlineColor, Colors.Black),
                    BlurRadius = item.OutlineThickness * 2 * sx,
                    ShadowDepth = 0,
                    Opacity = 1.0
                };
            }

            border.Child = tb;
            return border;
        }

        private FrameworkElement BuildShapeElement(ShapeOverlay item, double w, double h, double sx, double sy)
        {
            var strokeBrush = OverlayRenderer.TryParseBrush(item.StrokeColor, Brushes.Red);
            var fillBrush = OverlayRenderer.TryParseBrush(item.FillColor, Brushes.Transparent);
            var thickness = Math.Max(1, item.StrokeThickness * sx);

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
                        RadiusX = item.CornerRadius * sx,
                        RadiusY = item.CornerRadius * sy
                    };

                case ShapeType.Ellipse:
                    return new Ellipse
                    {
                        Width = w,
                        Height = h,
                        Stroke = strokeBrush,
                        Fill = fillBrush,
                        StrokeThickness = thickness
                    };

                case ShapeType.Arrow:
                    var arrowCanvas = new Canvas { Width = w, Height = h };
                    var line = new Line
                    {
                        X1 = thickness,
                        Y1 = h / 2.0,
                        X2 = w - 8 * sx,
                        Y2 = h / 2.0,
                        Stroke = strokeBrush,
                        StrokeThickness = thickness
                    };
                    var headSize = Math.Max(10, thickness * 3);
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

                default:
                    return new Line
                    {
                        X1 = thickness,
                        Y1 = h / 2.0,
                        X2 = w - thickness,
                        Y2 = h / 2.0,
                        Stroke = strokeBrush,
                        StrokeThickness = thickness
                    };
            }
        }

        private FrameworkElement BuildImageElement(ImageOverlay item, double w, double h)
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
                        Width = w,
                        Height = h,
                        Stretch = item.KeepAspectRatio ? Stretch.Uniform : Stretch.Fill
                    };
                }
                catch { }
            }

            return new Border
            {
                Width = w,
                Height = h,
                Background = new SolidColorBrush(Color.FromArgb(100, 100, 100, 100)),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = "画像が見つかりません",
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private void BuildSelectionAdorner(OverlayItem item, double sx, double sy)
        {
            var left = item.X * sx;
            var top = item.Y * sy;
            var w = item.Width * sx;
            var h = item.Height * sy;

            // Selection rectangle
            var selBorder = new Rectangle
            {
                Width = w,
                Height = h,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 3, 2 },
                IsHitTestVisible = false
            };
            Canvas.SetLeft(selBorder, left);
            Canvas.SetTop(selBorder, top);
            RootCanvas.Children.Add(selBorder);

            // 8 Handles
            AddHandle(left, top, DragMode.ResizeNW, Cursors.SizeNWSE);
            AddHandle(left + w / 2, top, DragMode.ResizeN, Cursors.SizeNS);
            AddHandle(left + w, top, DragMode.ResizeNE, Cursors.SizeNESW);
            AddHandle(left + w, top + h / 2, DragMode.ResizeE, Cursors.SizeWE);
            AddHandle(left + w, top + h, DragMode.ResizeSE, Cursors.SizeNWSE);
            AddHandle(left + w / 2, top + h, DragMode.ResizeS, Cursors.SizeNS);
            AddHandle(left, top + h, DragMode.ResizeSW, Cursors.SizeNESW);
            AddHandle(left, top + h / 2, DragMode.ResizeW, Cursors.SizeWE);
        }

        private void AddHandle(double x, double y, DragMode mode, Cursor cursor)
        {
            var handle = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = Brushes.White,
                Stroke = Brushes.DodgerBlue,
                StrokeThickness = 2,
                Cursor = cursor
            };

            Canvas.SetLeft(handle, x - HandleSize / 2);
            Canvas.SetTop(handle, y - HandleSize / 2);

            handle.MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left && SelectedItem is OverlayItem item)
                {
                    _dragMode = mode;
                    _dragStartMousePos = e.GetPosition(RootCanvas);
                    _dragStartItemRect = new Rect(item.X, item.Y, item.Width, item.Height);
                    RootCanvas.CaptureMouse();
                    e.Handled = true;
                }
            };

            RootCanvas.Children.Add(handle);
        }

        private void RootCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                // Trigger empty space click to notify parent (select video)
                CanvasEmptyClicked?.Invoke(this, EventArgs.Empty);
            }
        }

        private void RootCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragMode == DragMode.None || SelectedItem is not OverlayItem selectedOv) return;

            var currentMouse = e.GetPosition(RootCanvas);
            var deltaX = (currentMouse.X - _dragStartMousePos.X) / ScaleX;
            var deltaY = (currentMouse.Y - _dragStartMousePos.Y) / ScaleY;

            var r = _dragStartItemRect;

            switch (_dragMode)
            {
                case DragMode.Move:
                    selectedOv.X = Math.Round(r.X + deltaX);
                    selectedOv.Y = Math.Round(r.Y + deltaY);
                    break;

                case DragMode.ResizeSE:
                    selectedOv.Width = Math.Max(20, Math.Round(r.Width + deltaX));
                    selectedOv.Height = Math.Max(20, Math.Round(r.Height + deltaY));
                    break;

                case DragMode.ResizeE:
                    selectedOv.Width = Math.Max(20, Math.Round(r.Width + deltaX));
                    break;

                case DragMode.ResizeS:
                    selectedOv.Height = Math.Max(20, Math.Round(r.Height + deltaY));
                    break;

                case DragMode.ResizeNW:
                    var newW = Math.Max(20, r.Width - deltaX);
                    var newH = Math.Max(20, r.Height - deltaY);
                    selectedOv.X = Math.Round(r.Right - newW);
                    selectedOv.Y = Math.Round(r.Bottom - newH);
                    selectedOv.Width = Math.Round(newW);
                    selectedOv.Height = Math.Round(newH);
                    break;

                case DragMode.ResizeNE:
                    var wNE = Math.Max(20, r.Width + deltaX);
                    var hNE = Math.Max(20, r.Height - deltaY);
                    selectedOv.Y = Math.Round(r.Bottom - hNE);
                    selectedOv.Width = Math.Round(wNE);
                    selectedOv.Height = Math.Round(hNE);
                    break;

                case DragMode.ResizeSW:
                    var wSW = Math.Max(20, r.Width - deltaX);
                    var hSW = Math.Max(20, r.Height + deltaY);
                    selectedOv.X = Math.Round(r.Right - wSW);
                    selectedOv.Width = Math.Round(wSW);
                    selectedOv.Height = Math.Round(hSW);
                    break;

                case DragMode.ResizeW:
                    var wW = Math.Max(20, r.Width - deltaX);
                    selectedOv.X = Math.Round(r.Right - wW);
                    selectedOv.Width = Math.Round(wW);
                    break;

                case DragMode.ResizeN:
                    var hN = Math.Max(20, r.Height - deltaY);
                    selectedOv.Y = Math.Round(r.Bottom - hN);
                    selectedOv.Height = Math.Round(hN);
                    break;
            }

            InvalidateVisuals();
        }

        private void RootCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragMode != DragMode.None)
            {
                _dragMode = DragMode.None;
                RootCanvas.ReleaseMouseCapture();
            }
        }
    }
}
