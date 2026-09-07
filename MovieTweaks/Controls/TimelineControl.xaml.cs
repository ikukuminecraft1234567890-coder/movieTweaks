using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MovieTweaks.Models;

namespace MovieTweaks.Controls
{
    public partial class TimelineControl : UserControl
    {
        public static readonly DependencyProperty DurationProperty =
            DependencyProperty.Register(nameof(Duration), typeof(double), typeof(TimelineControl),
                new PropertyMetadata(10.0, OnVisualAffectingChanged));

        public static readonly DependencyProperty CurrentTimeProperty =
            DependencyProperty.Register(nameof(CurrentTime), typeof(double), typeof(TimelineControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCurrentTimeChanged));

        public static readonly DependencyProperty CutRangesProperty =
            DependencyProperty.Register(nameof(CutRanges), typeof(System.Collections.IEnumerable), typeof(TimelineControl),
                new PropertyMetadata(null, OnCutRangesChanged));

        public static readonly DependencyProperty OverlaysProperty =
            DependencyProperty.Register(nameof(Overlays), typeof(System.Collections.IEnumerable), typeof(TimelineControl),
                new PropertyMetadata(null, OnOverlaysChanged));

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(nameof(SelectedItem), typeof(OverlayItem), typeof(TimelineControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

        public static readonly DependencyProperty ZoomProperty =
            DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(TimelineControl),
                new PropertyMetadata(1.0, OnVisualAffectingChanged));

        public double Duration
        {
            get => (double)GetValue(DurationProperty);
            set => SetValue(DurationProperty, value);
        }

        public double CurrentTime
        {
            get => (double)GetValue(CurrentTimeProperty);
            set => SetValue(CurrentTimeProperty, value);
        }

        public System.Collections.IEnumerable? CutRanges
        {
            get => (System.Collections.IEnumerable?)GetValue(CutRangesProperty);
            set => SetValue(CutRangesProperty, value);
        }

        public System.Collections.IEnumerable? Overlays
        {
            get => (System.Collections.IEnumerable?)GetValue(OverlaysProperty);
            set => SetValue(OverlaysProperty, value);
        }

        public OverlayItem? SelectedItem
        {
            get => (OverlayItem?)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public double Zoom
        {
            get => (double)GetValue(ZoomProperty);
            set => SetValue(ZoomProperty, value);
        }

        // Interaction state
        private bool _isScrubbing;
        private enum BarDragMode { None, Slide, TrimLeft, TrimRight }
        private BarDragMode _barDragMode = BarDragMode.None;
        private OverlayItem? _draggingOverlay;
        private Point _dragStartPos;
        private double _dragStartStartTime;
        private double _dragStartEndTime;

        public TimelineControl()
        {
            InitializeComponent();
            SizeChanged += (s, e) => RedrawAll();
            Loaded += (s, e) => RedrawAll();
        }

        private static void OnVisualAffectingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimelineControl tc) tc.RedrawAll();
        }

        private static void OnCurrentTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimelineControl tc) tc.UpdatePlayhead();
        }

        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimelineControl tc) tc.RedrawOverlays();
        }

        private static void OnCutRangesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimelineControl tc)
            {
                if (e.OldValue is INotifyCollectionChanged oldList)
                    oldList.CollectionChanged -= tc.OnCutRangesCollectionChanged;
                if (e.NewValue is INotifyCollectionChanged newList)
                    newList.CollectionChanged += tc.OnCutRangesCollectionChanged;

                tc.RedrawVideoTrack();
            }
        }

        private void OnCutRangesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RedrawVideoTrack();
        }

        private static void OnOverlaysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TimelineControl tc)
            {
                if (e.OldValue is INotifyCollectionChanged oldList)
                    oldList.CollectionChanged -= tc.OnOverlaysCollectionChanged;
                if (e.NewValue is INotifyCollectionChanged newList)
                    newList.CollectionChanged += tc.OnOverlaysCollectionChanged;

                tc.RedrawOverlays();
            }
        }

        private void OnOverlaysCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (OverlayItem item in e.NewItems) item.PropertyChanged += OnOverlayItemPropertyChanged;
            }
            if (e.OldItems != null)
            {
                foreach (OverlayItem item in e.OldItems) item.PropertyChanged -= OnOverlayItemPropertyChanged;
            }
            RedrawOverlays();
        }

        private void OnOverlayItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OverlayItem.StartTime) || e.PropertyName == nameof(OverlayItem.EndTime) || e.PropertyName == nameof(OverlayItem.Name))
            {
                RedrawOverlays();
            }
        }

        private double TimeToX(double seconds)
        {
            if (Duration <= 0 || ActualWidth <= 0) return 0;
            return (seconds / Duration) * ActualWidth;
        }

        private double XToTime(double x)
        {
            if (ActualWidth <= 0 || Duration <= 0) return 0;
            return Math.Clamp((x / ActualWidth) * Duration, 0, Duration);
        }

        public void RedrawAll()
        {
            RedrawRuler();
            RedrawVideoTrack();
            RedrawOverlays();
            UpdatePlayhead();
        }

        private void RedrawRuler()
        {
            RulerCanvas.Children.Clear();
            if (Duration <= 0 || ActualWidth <= 0) return;

            // Step calculation based on duration & width
            double step = 1.0;
            if (Duration > 60) step = 10.0;
            if (Duration > 300) step = 30.0;
            if (Duration > 600) step = 60.0;
            if (Duration < 10) step = 0.5;

            for (double t = 0; t <= Duration; t += step)
            {
                double x = TimeToX(t);

                // Tick
                var line = new Line
                {
                    X1 = x,
                    Y1 = 14,
                    X2 = x,
                    Y2 = 24,
                    Stroke = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                    StrokeThickness = 1
                };
                RulerCanvas.Children.Add(line);

                // Label
                var text = new TextBlock
                {
                    Text = FormatRulerTime(t),
                    Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                    FontSize = 10
                };
                Canvas.SetLeft(text, x + 2);
                Canvas.SetTop(text, 1);
                RulerCanvas.Children.Add(text);
            }
        }

        private string FormatRulerTime(double seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalMinutes >= 1 ? $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}" : $"{ts.Seconds}s";
        }

        private void RedrawVideoTrack()
        {
            VideoTrackCanvas.Children.Clear();
            if (Duration <= 0 || ActualWidth <= 0) return;

            // Draw base video track bar
            var baseRect = new Rectangle
            {
                Width = ActualWidth,
                Height = 28,
                Fill = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                RadiusX = 3,
                RadiusY = 3
            };
            Canvas.SetLeft(baseRect, 0);
            Canvas.SetTop(baseRect, 4);
            VideoTrackCanvas.Children.Add(baseRect);

            // Draw kept ranges
            if (CutRanges != null)
            {
                foreach (var obj in CutRanges)
                {
                    if (obj is not CutRange r || !r.IsKeep) continue;
                    double x1 = TimeToX(r.StartSeconds);
                    double x2 = TimeToX(r.EndSeconds);
                    double w = Math.Max(2, x2 - x1);

                    var keepBar = new Rectangle
                    {
                        Width = w,
                        Height = 26,
                        Fill = new SolidColorBrush(Color.FromArgb(200, 0, 122, 204)), // VS Blue
                        Stroke = new SolidColorBrush(Color.FromRgb(0, 150, 255)),
                        StrokeThickness = 1,
                        RadiusX = 2,
                        RadiusY = 2
                    };
                    Canvas.SetLeft(keepBar, x1);
                    Canvas.SetTop(keepBar, 5);
                    VideoTrackCanvas.Children.Add(keepBar);

                    // Cut range text
                    var lbl = new TextBlock
                    {
                        Text = $"{r.Duration:F1}s",
                        Foreground = Brushes.White,
                        FontSize = 10,
                        Margin = new Thickness(4, 2, 0, 0)
                    };
                    Canvas.SetLeft(lbl, x1);
                    Canvas.SetTop(lbl, 8);
                    VideoTrackCanvas.Children.Add(lbl);
                }
            }
        }

        private void RedrawOverlays()
        {
            OverlayTrackCanvas.Children.Clear();
            if (Duration <= 0 || ActualWidth <= 0 || Overlays == null) return;

            int rowHeight = 26;
            int currentY = 4;

            foreach (var obj in Overlays)
            {
                if (obj is not OverlayItem item) continue;

                double x1 = TimeToX(item.StartTime);
                double x2 = TimeToX(item.EndTime);
                double w = Math.Max(10, x2 - x1);

                bool isSel = item == SelectedItem;

                var barBrush = item switch
                {
                    TextOverlay => new SolidColorBrush(Color.FromRgb(220, 120, 50)), // Orange
                    ShapeOverlay => new SolidColorBrush(Color.FromRgb(200, 60, 100)), // Magenta
                    ImageOverlay => new SolidColorBrush(Color.FromRgb(60, 160, 90)), // Green
                    _ => new SolidColorBrush(Colors.SteelBlue)
                };

                var border = new Border
                {
                    Width = w,
                    Height = rowHeight,
                    Background = barBrush,
                    BorderBrush = isSel ? Brushes.White : new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                    BorderThickness = new Thickness(isSel ? 2 : 1),
                    CornerRadius = new CornerRadius(3),
                    Tag = item,
                    Cursor = Cursors.Hand
                };

                // Label inside bar
                var tb = new TextBlock
                {
                    Text = item.Name,
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = isSel ? FontWeights.Bold : FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 8, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                border.Child = tb;

                Canvas.SetLeft(border, x1);
                Canvas.SetTop(border, currentY);

                // Mouse interaction for the bar
                border.MouseDown += (s, e) =>
                {
                    if (e.ChangedButton == MouseButton.Left)
                    {
                        SelectedItem = item;
                        _draggingOverlay = item;
                        _dragStartPos = e.GetPosition(OverlayTrackCanvas);
                        _dragStartStartTime = item.StartTime;
                        _dragStartEndTime = item.EndTime;

                        // Check if clicking near left or right edges for trimming
                        var localX = e.GetPosition(border).X;
                        if (localX < 6)
                        {
                            _barDragMode = BarDragMode.TrimLeft;
                            border.Cursor = Cursors.SizeWE;
                        }
                        else if (localX > border.ActualWidth - 6)
                        {
                            _barDragMode = BarDragMode.TrimRight;
                            border.Cursor = Cursors.SizeWE;
                        }
                        else
                        {
                            _barDragMode = BarDragMode.Slide;
                            border.Cursor = Cursors.SizeAll;
                        }

                        OverlayTrackCanvas.CaptureMouse();
                        e.Handled = true;
                    }
                };

                OverlayTrackCanvas.Children.Add(border);
                currentY += rowHeight + 4;
            }

            OverlayTrackCanvas.Height = Math.Max(60, currentY + 10);
        }

        private void UpdatePlayhead()
        {
            double x = TimeToX(CurrentTime);
            PlayheadLine.X1 = x;
            PlayheadLine.X2 = x;
            PlayheadLine.Y2 = ActualHeight;
            Canvas.SetLeft(PlayheadHead, x - 6);
        }

        private void Ruler_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isScrubbing = true;
                RulerCanvas.CaptureMouse();
                SeekToMouse(e.GetPosition(RulerCanvas).X);
            }
        }

        private void Ruler_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isScrubbing)
            {
                SeekToMouse(e.GetPosition(RulerCanvas).X);
            }
        }

        private void Ruler_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isScrubbing)
            {
                _isScrubbing = false;
                RulerCanvas.ReleaseMouseCapture();
            }
        }

        private void Track_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                SeekToMouse(e.GetPosition(VideoTrackCanvas).X);
            }
        }

        private void SeekToMouse(double x)
        {
            CurrentTime = XToTime(x);
        }

        private void OverlayTrack_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && _barDragMode == BarDragMode.None)
            {
                // Click on empty track area seeks
                SeekToMouse(e.GetPosition(OverlayTrackCanvas).X);
                SelectedItem = null;
            }
        }

        private void OverlayTrack_MouseMove(object sender, MouseEventArgs e)
        {
            if (_barDragMode != BarDragMode.None && _draggingOverlay != null)
            {
                var curPos = e.GetPosition(OverlayTrackCanvas);
                var deltaSec = XToTime(curPos.X) - XToTime(_dragStartPos.X);

                switch (_barDragMode)
                {
                    case BarDragMode.Slide:
                        var duration = _dragStartEndTime - _dragStartStartTime;
                        var newStart = Math.Clamp(_dragStartStartTime + deltaSec, 0, Duration - duration);
                        _draggingOverlay.StartTime = newStart;
                        _draggingOverlay.EndTime = newStart + duration;
                        break;

                    case BarDragMode.TrimLeft:
                        var trimmedStart = Math.Clamp(_dragStartStartTime + deltaSec, 0, _draggingOverlay.EndTime - 0.2);
                        _draggingOverlay.StartTime = trimmedStart;
                        break;

                    case BarDragMode.TrimRight:
                        var trimmedEnd = Math.Clamp(_dragStartEndTime + deltaSec, _draggingOverlay.StartTime + 0.2, Duration);
                        _draggingOverlay.EndTime = trimmedEnd;
                        break;
                }

                RedrawOverlays();
            }
        }

        private void OverlayTrack_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_barDragMode != BarDragMode.None)
            {
                _barDragMode = BarDragMode.None;
                _draggingOverlay = null;
                OverlayTrackCanvas.ReleaseMouseCapture();
            }
        }
    }
}
