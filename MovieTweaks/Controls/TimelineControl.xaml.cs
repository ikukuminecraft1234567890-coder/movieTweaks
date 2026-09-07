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
using MovieTweaks.ViewModels;

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

        public static readonly DependencyProperty IsScrubbingProperty =
            DependencyProperty.Register(nameof(IsScrubbing), typeof(bool), typeof(TimelineControl),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty CutRangesProperty =
            DependencyProperty.Register(nameof(CutRanges), typeof(System.Collections.IEnumerable), typeof(TimelineControl),
                new PropertyMetadata(null, OnCutRangesChanged));

        public static readonly DependencyProperty OverlaysProperty =
            DependencyProperty.Register(nameof(Overlays), typeof(System.Collections.IEnumerable), typeof(TimelineControl),
                new PropertyMetadata(null, OnOverlaysChanged));

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(TimelineControl),
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

        public bool IsScrubbing
        {
            get => (bool)GetValue(IsScrubbingProperty);
            set => SetValue(IsScrubbingProperty, value);
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

        public object? SelectedItem
        {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public double Zoom
        {
            get => (double)GetValue(ZoomProperty);
            set => SetValue(ZoomProperty, value);
        }

        // Interaction state
        private enum BarDragMode { None, Slide, TrimLeft, TrimRight }
        private BarDragMode _barDragMode = BarDragMode.None;
        private OverlayItem? _draggingOverlay;
        private CutRange? _draggingCutRange;
        private Point _dragStartPos;
        private double _dragStartStartTime;
        private double _dragStartEndTime;
        private double _dragStartSourceStart;

        private readonly System.Windows.Threading.DispatcherTimer _scrubThrottler;
        private double _pendingScrubTime = -1;

        public TimelineControl()
        {
            InitializeComponent();

            _scrubThrottler = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };
            _scrubThrottler.Tick += (s, e) =>
            {
                _scrubThrottler.Stop();
                if (_pendingScrubTime >= 0)
                {
                    double t = _pendingScrubTime;
                    _pendingScrubTime = -1;
                    CurrentTime = t;
                }
            };

            RulerCanvas.LostMouseCapture += (s, e) =>
            {
                if (IsScrubbing)
                {
                    IsScrubbing = false;
                }
            };
            VideoTrackCanvas.LostMouseCapture += (s, e) =>
            {
                if (_barDragMode != BarDragMode.None)
                {
                    _barDragMode = BarDragMode.None;
                    _draggingCutRange = null;
                    IsScrubbing = false;
                    RedrawAll();
                }
            };
            OverlayTrackCanvas.LostMouseCapture += (s, e) =>
            {
                if (_barDragMode != BarDragMode.None)
                {
                    _barDragMode = BarDragMode.None;
                    _draggingOverlay = null;
                    IsScrubbing = false;
                    RedrawOverlays();
                }
            };

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
            if (d is TimelineControl tc)
            {
                tc.RedrawVideoTrack();
                tc.RedrawOverlays();
            }
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
            if (e.NewItems != null)
            {
                foreach (CutRange r in e.NewItems) r.PropertyChanged += OnCutRangePropertyChanged;
            }
            if (e.OldItems != null)
            {
                foreach (CutRange r in e.OldItems) r.PropertyChanged -= OnCutRangePropertyChanged;
            }
            RedrawVideoTrack();
        }

        private void OnCutRangePropertyChanged(object? sender, PropertyChangedEventArgs e)
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

            double step = 1.0;
            if (Duration > 60) step = 10.0;
            if (Duration > 300) step = 30.0;
            if (Duration > 600) step = 60.0;
            if (Duration < 10) step = 0.5;

            for (double t = 0; t <= Duration; t += step)
            {
                double x = TimeToX(t);

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

            // Background empty track
            var baseRect = new Rectangle
            {
                Width = ActualWidth,
                Height = 30,
                Fill = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                RadiusX = 3,
                RadiusY = 3
            };
            Canvas.SetLeft(baseRect, 0);
            Canvas.SetTop(baseRect, 3);
            VideoTrackCanvas.Children.Add(baseRect);

            // Draw each cut range / video segment
            if (CutRanges != null)
            {
                foreach (var obj in CutRanges)
                {
                    if (obj is not CutRange r || !r.IsKeep) continue;
                    double x1 = TimeToX(r.StartSeconds);
                    double x2 = TimeToX(r.EndSeconds);
                    double w = Math.Max(12, x2 - x1);

                    bool isSel = (SelectedItem == r) || (SelectedItem is VideoClip);

                    var clipBorder = new Border
                    {
                        Width = w,
                        Height = 28,
                        Background = new SolidColorBrush(Color.FromRgb(16, 124, 65)), // Video Clip Green / Teal
                        BorderBrush = isSel ? Brushes.White : new SolidColorBrush(Color.FromArgb(180, 50, 205, 120)),
                        BorderThickness = new Thickness(isSel ? 2 : 1),
                        CornerRadius = new CornerRadius(3),
                        Cursor = Cursors.SizeAll,
                        ToolTip = $"動画クリップ [{r.StartSeconds:F1}s - {r.EndSeconds:F1}s (元動画: {r.SourceStartSeconds:F1}s~)] (中央ドラッグで移動・端ドラッグでトリミング)"
                    };

                    clipBorder.MouseMove += (s, e) =>
                    {
                        if (_barDragMode == BarDragMode.None)
                        {
                            var localX = e.GetPosition(clipBorder).X;
                            if (localX < 8 || localX > clipBorder.ActualWidth - 8)
                            {
                                clipBorder.Cursor = Cursors.SizeWE;
                            }
                            else
                            {
                                clipBorder.Cursor = Cursors.SizeAll;
                            }
                        }
                    };

                    var clipContent = new Grid();
                    var lbl = new TextBlock
                    {
                        Text = $"🎬 動画 ({r.Duration:F1}s)",
                        Foreground = Brushes.White,
                        FontSize = 11,
                        FontWeight = isSel ? FontWeights.Bold : FontWeights.Normal,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 8, 0)
                    };
                    clipContent.Children.Add(lbl);
                    clipBorder.Child = clipContent;

                    Canvas.SetLeft(clipBorder, x1);
                    Canvas.SetTop(clipBorder, 4);

                    // Click to select clip and handle dragging / edge trimming
                    clipBorder.MouseDown += (s, e) =>
                    {
                        if (e.ChangedButton == MouseButton.Left)
                        {
                            SelectedItem = r;
                            _draggingCutRange = r;
                            _dragStartPos = e.GetPosition(VideoTrackCanvas);
                            _dragStartStartTime = r.StartSeconds;
                            _dragStartEndTime = r.EndSeconds;
                            _dragStartSourceStart = r.SourceStartSeconds;

                            var localX = e.GetPosition(clipBorder).X;
                            if (localX < 8)
                            {
                                _barDragMode = BarDragMode.TrimLeft;
                                clipBorder.Cursor = Cursors.SizeWE;
                            }
                            else if (localX > clipBorder.ActualWidth - 8)
                            {
                                _barDragMode = BarDragMode.TrimRight;
                                clipBorder.Cursor = Cursors.SizeWE;
                            }
                            else
                            {
                                _barDragMode = BarDragMode.Slide;
                                clipBorder.Cursor = Cursors.SizeAll;
                            }

                            IsScrubbing = true;
                            VideoTrackCanvas.CaptureMouse();
                            e.Handled = true;
                        }
                    };

                    VideoTrackCanvas.Children.Add(clipBorder);
                }
            }
        }

        private void RedrawOverlays()
        {
            OverlayTrackCanvas.Children.Clear();
            if (Duration <= 0 || ActualWidth <= 0 || Overlays == null) return;

            int rowHeight = 24;
            int currentY = 4;

            foreach (var obj in Overlays)
            {
                if (obj is not OverlayItem item) continue;

                double x1 = TimeToX(item.StartTime);
                double x2 = TimeToX(item.EndTime);
                double w = Math.Max(12, x2 - x1);

                bool isSel = item == SelectedItem;

                var barBrush = item switch
                {
                    TextOverlay => new SolidColorBrush(Color.FromRgb(220, 120, 50)), // Orange
                    ShapeOverlay => new SolidColorBrush(Color.FromRgb(200, 60, 100)), // Magenta
                    ImageOverlay => new SolidColorBrush(Color.FromRgb(0, 122, 204)), // Blue
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

                border.MouseDown += (s, e) =>
                {
                    if (e.ChangedButton == MouseButton.Left)
                    {
                        SelectedItem = item;
                        _draggingOverlay = item;
                        _dragStartPos = e.GetPosition(OverlayTrackCanvas);
                        _dragStartStartTime = item.StartTime;
                        _dragStartEndTime = item.EndTime;

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

                        IsScrubbing = true;
                        OverlayTrackCanvas.CaptureMouse();
                        e.Handled = true;
                    }
                };

                OverlayTrackCanvas.Children.Add(border);
                currentY += rowHeight + 4;
            }

            OverlayTrackCanvas.Height = Math.Max(60, currentY + 10);
        }

        private void UpdatePlayheadAt(double time)
        {
            double x = TimeToX(time);
            PlayheadLine.X1 = x;
            PlayheadLine.X2 = x;
            PlayheadLine.Y2 = ActualHeight;
            Canvas.SetLeft(PlayheadHead, x - 6);
        }

        private void UpdatePlayhead()
        {
            if (!IsScrubbing)
            {
                UpdatePlayheadAt(CurrentTime);
            }
        }

        private void Ruler_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                IsScrubbing = true;
                RulerCanvas.CaptureMouse();
                SeekToMouse(e.GetPosition(RulerCanvas).X, false);
            }
        }

        private void Ruler_MouseMove(object sender, MouseEventArgs e)
        {
            if (IsScrubbing)
            {
                SeekToMouse(e.GetPosition(RulerCanvas).X, false);
            }
        }

        private void Ruler_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (IsScrubbing)
            {
                RulerCanvas.ReleaseMouseCapture();
                SeekToMouse(e.GetPosition(RulerCanvas).X, true);
                IsScrubbing = false;
            }
        }

        private void Track_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && _barDragMode == BarDragMode.None)
            {
                SeekToMouse(e.GetPosition(VideoTrackCanvas).X, true);
            }
        }

        private void VideoTrack_MouseMove(object sender, MouseEventArgs e)
        {
            if (_barDragMode != BarDragMode.None && _draggingCutRange != null)
            {
                var curPos = e.GetPosition(VideoTrackCanvas);
                var deltaSec = XToTime(curPos.X) - XToTime(_dragStartPos.X);

                if (_barDragMode == BarDragMode.TrimLeft)
                {
                    double newStart = Math.Clamp(_dragStartStartTime + deltaSec, 0, _draggingCutRange.EndSeconds - 0.2);
                    double actualDelta = newStart - _dragStartStartTime;
                    _draggingCutRange.StartSeconds = newStart;
                    _draggingCutRange.SourceStartSeconds = Math.Max(0, _dragStartSourceStart + actualDelta);
                    CurrentTime = _draggingCutRange.StartSeconds;
                }
                else if (_barDragMode == BarDragMode.TrimRight)
                {
                    _draggingCutRange.EndSeconds = Math.Max(_draggingCutRange.StartSeconds + 0.2, _dragStartEndTime + deltaSec);
                    CurrentTime = _draggingCutRange.EndSeconds;
                }
                else if (_barDragMode == BarDragMode.Slide)
                {
                    double dur = _dragStartEndTime - _dragStartStartTime;
                    double newStart = Math.Max(0, _dragStartStartTime + deltaSec);
                    _draggingCutRange.StartSeconds = newStart;
                    _draggingCutRange.EndSeconds = newStart + dur;
                    CurrentTime = newStart;
                }
                RedrawVideoTrack();
            }
        }

        private void VideoTrack_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingCutRange != null)
            {
                _draggingCutRange = null;
                _barDragMode = BarDragMode.None;
                IsScrubbing = false;
                VideoTrackCanvas.ReleaseMouseCapture();
                RedrawAll();
            }
        }

        private void SeekToMouse(double x, bool isFinal = true)
        {
            double t = Math.Clamp(XToTime(x), 0, Math.Max(0.1, Duration));
            // Move red line visually at 60fps immediately!
            UpdatePlayheadAt(t);

            if (isFinal)
            {
                _scrubThrottler.Stop();
                _pendingScrubTime = -1;
                CurrentTime = t;
            }
            else
            {
                _pendingScrubTime = t;
                if (!_scrubThrottler.IsEnabled)
                {
                    _scrubThrottler.Start();
                }
            }
        }

        private void OverlayTrack_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && _barDragMode == BarDragMode.None)
            {
                SeekToMouse(e.GetPosition(OverlayTrackCanvas).X, true);
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
                        CurrentTime = trimmedStart;
                        break;

                    case BarDragMode.TrimRight:
                        var trimmedEnd = Math.Clamp(_dragStartEndTime + deltaSec, _draggingOverlay.StartTime + 0.2, Duration);
                        _draggingOverlay.EndTime = trimmedEnd;
                        CurrentTime = trimmedEnd;
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
                IsScrubbing = false;
                OverlayTrackCanvas.ReleaseMouseCapture();
            }
        }

        private void TimelineControl_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Right-click instantly seeks to the clicked timestamp!
            var p = e.GetPosition(this);
            SeekToMouse(p.X);
        }

        private MainViewModel? GetViewModel() => DataContext as MainViewModel;

        private void MenuSplit_Click(object sender, RoutedEventArgs e)
        {
            GetViewModel()?.SplitCutCommand.Execute(null);
        }

        private void MenuSetIn_Click(object sender, RoutedEventArgs e)
        {
            GetViewModel()?.SetInPointCommand.Execute(null);
        }

        private void MenuSetOut_Click(object sender, RoutedEventArgs e)
        {
            GetViewModel()?.SetOutPointCommand.Execute(null);
        }

        private void MenuAddText_Click(object sender, RoutedEventArgs e)
        {
            GetViewModel()?.AddTextOverlayCommand.Execute(null);
        }

        private void MenuAddRect_Click(object sender, RoutedEventArgs e)
        {
            GetViewModel()?.AddShapeOverlayCommand.Execute(ShapeType.Rectangle);
        }

        private void MenuAddArrow_Click(object sender, RoutedEventArgs e)
        {
            GetViewModel()?.AddShapeOverlayCommand.Execute(ShapeType.Arrow);
        }

        private void MenuAddImage_Click(object sender, RoutedEventArgs e)
        {
            GetViewModel()?.AddImageOverlayCommand.Execute(null);
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            GetViewModel()?.DeleteSelectedCommand.Execute(null);
        }

        private void MenuResetCut_Click(object sender, RoutedEventArgs e)
        {
            GetViewModel()?.ResetCutCommand.Execute(null);
        }
    }
}
