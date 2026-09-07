using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MovieTweaks.Models;
using MovieTweaks.Services;

namespace MovieTweaks.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly FFmpegService _ffmpegService;
        private readonly DispatcherTimer _playbackTimer;
        private readonly UndoRedoService _undoRedo = new();
        private DateTime _lastPlaybackTick;

        private Project _project = new();
        private double _currentTimeSeconds;
        private bool _isPlaying;
        private bool _isExporting;
        private double _exportProgress;
        private string _statusMessage = "動画ファイルをドラッグ＆ドロップするか、「動画を開く」をクリックしてください。";
        private CancellationTokenSource? _exportCts;
        private double _timelineZoom = 1.0; // 1.0 = 100 pixels per 10 sec etc.

        public bool CanUndo => _undoRedo.CanUndo;
        public bool CanRedo => _undoRedo.CanRedo;

        public Project Project
        {
            get => _project;
            set
            {
                if (SetProperty(ref _project, value))
                {
                    SetupCollectionListeners(_project);
                    OnPropertyChanged(nameof(TotalDurationSeconds));
                    OnPropertyChanged(nameof(FormattedTotalTime));
                    OnPropertyChanged(nameof(CurrentTimeDisplay));
                }
            }
        }

        public double CurrentTimeSeconds
        {
            get => _currentTimeSeconds;
            set
            {
                double clamped = Math.Max(0, value);
                if (Math.Abs(_currentTimeSeconds - clamped) > 0.001)
                {
                    _currentTimeSeconds = clamped;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FormattedCurrentTime));
                    OnPropertyChanged(nameof(CurrentTimeDisplay));
                    RequestMediaSeek?.Invoke(_currentTimeSeconds);
                }
            }
        }

        public void RecordHistory()
        {
            _undoRedo.RecordState(Project);
        }

        public void Undo()
        {
            if (_undoRedo.Undo(Project))
            {
                StatusMessage = "操作を元に戻しました (Ctrl+Z)";
                OnPropertyChanged(nameof(TotalDurationSeconds));
                OnPropertyChanged(nameof(FormattedTotalTime));
                OnPropertyChanged(nameof(CurrentTimeDisplay));
                SelectedItem = Project.Overlays.LastOrDefault() ?? (object?)Project.SourceVideo;
                RequestMediaSeek?.Invoke(CurrentTimeSeconds);
            }
        }

        public void Redo()
        {
            if (_undoRedo.Redo(Project))
            {
                StatusMessage = "操作をやり直しました (Ctrl+Y)";
                OnPropertyChanged(nameof(TotalDurationSeconds));
                OnPropertyChanged(nameof(FormattedTotalTime));
                OnPropertyChanged(nameof(CurrentTimeDisplay));
                SelectedItem = Project.Overlays.LastOrDefault() ?? (object?)Project.SourceVideo;
                RequestMediaSeek?.Invoke(CurrentTimeSeconds);
            }
        }

        public double TotalDurationSeconds
        {
            get
            {
                double maxTime = Project.SourceVideo?.DurationSeconds ?? 0;
                if (Project.CutRanges != null)
                {
                    foreach (var r in Project.CutRanges)
                    {
                        if (r.EndSeconds > maxTime) maxTime = r.EndSeconds;
                    }
                }
                if (Project.Overlays != null)
                {
                    foreach (var o in Project.Overlays)
                    {
                        if (o.EndTime > maxTime) maxTime = o.EndTime;
                    }
                }
                return Math.Max(maxTime, 5.0);
            }
        }

        public string FormattedCurrentTime => FormatTime(CurrentTimeSeconds);
        public string FormattedTotalTime => FormatTime(TotalDurationSeconds);
        public string CurrentTimeDisplay => $"{FormattedCurrentTime} / {FormattedTotalTime}";

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (SetProperty(ref _isPlaying, value))
                {
                    OnPropertyChanged(nameof(PlayPauseButtonText));
                    if (value)
                    {
                        _lastPlaybackTick = DateTime.UtcNow;
                        _playbackTimer.Start();
                    }
                    else
                    {
                        _playbackTimer.Stop();
                    }
                }
            }
        }

        private bool _isScrubbing;
        private bool _wasPlayingBeforeScrub;

        public bool IsScrubbing
        {
            get => _isScrubbing;
            set
            {
                if (SetProperty(ref _isScrubbing, value))
                {
                    RequestScrubStateChange?.Invoke(value);
                    if (value)
                    {
                        _wasPlayingBeforeScrub = IsPlaying;
                        if (IsPlaying)
                        {
                            _playbackTimer.Stop();
                        }
                    }
                    else
                    {
                        if (_wasPlayingBeforeScrub)
                        {
                            _wasPlayingBeforeScrub = false;
                            if (IsPlaying)
                            {
                                _lastPlaybackTick = DateTime.UtcNow;
                                _playbackTimer.Start();
                                RequestMediaPlay?.Invoke();
                            }
                        }
                    }
                }
            }
        }

        public void SetCurrentTimeInternal(double value)
        {
            double clamped = Math.Clamp(value, 0, Math.Max(0.1, TotalDurationSeconds));
            if (Math.Abs(_currentTimeSeconds - clamped) > 0.001)
            {
                _currentTimeSeconds = clamped;
                OnPropertyChanged(nameof(CurrentTimeSeconds));
                OnPropertyChanged(nameof(FormattedCurrentTime));
                OnPropertyChanged(nameof(CurrentTimeDisplay));
            }
        }

        public string PlayPauseButtonText => IsPlaying ? "一時停止" : "再生";

        private object? _selectedItem;

        public object? SelectedItem
        {
            get => _selectedItem;
            set
            {
                // Deselect previous
                if (_selectedItem is OverlayItem oldOv) oldOv.IsSelected = false;
                if (_selectedItem is VideoClip oldVc) oldVc.IsSelected = false;
                if (_selectedItem is CutRange oldCr) oldCr.IsSelected = false;

                if (SetProperty(ref _selectedItem, value))
                {
                    if (_selectedItem is OverlayItem newOv) newOv.IsSelected = true;
                    if (_selectedItem is VideoClip newVc) newVc.IsSelected = true;
                    if (_selectedItem is CutRange newCr) newCr.IsSelected = true;

                    OnPropertyChanged(nameof(SelectedOverlay));
                    OnPropertyChanged(nameof(SelectedVideoClip));
                    OnPropertyChanged(nameof(SelectedCutRange));
                    OnPropertyChanged(nameof(SelectedTextOverlay));
                    OnPropertyChanged(nameof(SelectedShapeOverlay));
                    OnPropertyChanged(nameof(SelectedImageOverlay));
                    OnPropertyChanged(nameof(CurrentVolumePercent));
                    OnPropertyChanged(nameof(CurrentPlaybackSpeedPercent));
                    OnPropertyChanged(nameof(CurrentOpacityPercent));
                    OnPropertyChanged(nameof(CurrentScalePercent));
                    OnPropertyChanged(nameof(CurrentRotationDegrees));
                    OnPropertyChanged(nameof(HasSelection));
                    OnPropertyChanged(nameof(HasSelectedOverlay));
                    OnPropertyChanged(nameof(IsVideoSelected));
                    OnPropertyChanged(nameof(IsCutRangeSelected));
                    OnPropertyChanged(nameof(IsTextSelected));
                    OnPropertyChanged(nameof(IsShapeSelected));
                    OnPropertyChanged(nameof(IsImageSelected));
                }
            }
        }

        public OverlayItem? SelectedOverlay
        {
            get => SelectedItem as OverlayItem;
            set => SelectedItem = value;
        }

        public VideoClip? SelectedVideoClip => SelectedItem as VideoClip ?? (SelectedItem is CutRange ? Project.SourceVideo : null);
        public CutRange? SelectedCutRange => SelectedItem as CutRange;
        public TextOverlay? SelectedTextOverlay => SelectedItem as TextOverlay;
        public ShapeOverlay? SelectedShapeOverlay => SelectedItem as ShapeOverlay;
        public ImageOverlay? SelectedImageOverlay => SelectedItem as ImageOverlay;

        public double CurrentVolumePercent
        {
            get
            {
                if (SelectedCutRange != null) return SelectedCutRange.VolumePercent;
                if (Project.SourceVideo != null) return Project.SourceVideo.VolumePercent;
                return 100.0;
            }
            set
            {
                value = Math.Clamp(value, 0.0, 2000.0);
                if (SelectedCutRange != null)
                {
                    SelectedCutRange.VolumePercent = value;
                }
                else
                {
                    if (Project.CutRanges != null)
                    {
                        foreach (var r in Project.CutRanges)
                        {
                            r.VolumePercent = value;
                        }
                    }
                }
                if (Project.SourceVideo != null)
                {
                    Project.SourceVideo.VolumePercent = value;
                }
                OnPropertyChanged();
                RequestMediaSeek?.Invoke(CurrentTimeSeconds);
            }
        }

        public double CurrentPlaybackSpeedPercent
        {
            get
            {
                if (SelectedCutRange != null) return SelectedCutRange.PlaybackSpeedPercent;
                if (Project.SourceVideo != null) return Project.SourceVideo.PlaybackSpeedPercent;
                return 100.0;
            }
            set
            {
                value = Math.Clamp(value, 1.0, 10000.0);
                double speed = value / 100.0;
                if (SelectedCutRange != null)
                {
                    SetClipPlaybackSpeed(SelectedCutRange, speed);
                }
                else
                {
                    if (Project.CutRanges != null && Project.CutRanges.Count > 0)
                    {
                        foreach (var r in Project.CutRanges.ToList())
                        {
                            SetClipPlaybackSpeed(r, speed);
                        }
                    }
                }
                if (Project.SourceVideo != null)
                {
                    Project.SourceVideo.PlaybackSpeedPercent = value;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentPlaybackSpeedPercent));
            }
        }

        public void SetClipPlaybackSpeed(CutRange clip, double newSpeed)
        {
            if (newSpeed <= 0.005 || Math.Abs(clip.PlaybackSpeed - newSpeed) < 0.0001) return;

            RecordHistory();
            double sourceDuration = clip.Duration * clip.PlaybackSpeed;
            double oldEnd = clip.EndSeconds;
            clip.PlaybackSpeed = newSpeed;
            double newEnd = clip.StartSeconds + (sourceDuration / newSpeed);
            double delta = newEnd - oldEnd;
            clip.EndSeconds = newEnd;

            // Shift subsequent clips if they were adjacent or after oldEnd
            if (Project.CutRanges != null)
            {
                foreach (var other in Project.CutRanges.Where(r => r != clip && r.StartSeconds >= oldEnd - 0.02))
                {
                    other.StartSeconds += delta;
                    other.EndSeconds += delta;
                }
            }

            OnPropertyChanged(nameof(TotalDurationSeconds));
            OnPropertyChanged(nameof(FormattedTotalTime));
            OnPropertyChanged(nameof(CurrentTimeDisplay));
            RequestMediaSeek?.Invoke(CurrentTimeSeconds);
        }

        public double CurrentOpacityPercent
        {
            get
            {
                if (SelectedOverlay != null) return SelectedOverlay.OpacityPercent;
                if (Project.SourceVideo != null) return Project.SourceVideo.OpacityPercent;
                return 100.0;
            }
            set
            {
                value = Math.Clamp(value, 0.0, 100.0);
                if (SelectedOverlay != null)
                {
                    SelectedOverlay.OpacityPercent = value;
                }
                if (Project.SourceVideo != null)
                {
                    Project.SourceVideo.OpacityPercent = value;
                }
                OnPropertyChanged();
            }
        }

        public double CurrentScalePercent
        {
            get => SelectedOverlay?.ScalePercent ?? 100.0;
            set
            {
                if (SelectedOverlay != null)
                {
                    value = Math.Clamp(value, 5.0, 2000.0);
                    SelectedOverlay.ScalePercent = value;
                    OnPropertyChanged();
                }
            }
        }

        public double CurrentRotationDegrees
        {
            get => SelectedOverlay?.Rotation ?? 0.0;
            set
            {
                if (SelectedOverlay != null)
                {
                    value = (value % 360.0 + 360.0) % 360.0;
                    SelectedOverlay.Rotation = Math.Round(value, 1);
                    OnPropertyChanged();
                }
            }
        }

        private static readonly string[] PriorityFonts =
        {
            "Yu Gothic UI",
            "Meiryo",
            "MS Gothic",
            "MS PGothic",
            "BIZ UDPGothic",
            "Yu Mincho",
            "Impact",
            "Arial",
            "Segoe UI",
            "Comic Sans MS",
            "Consolas"
        };

        public IReadOnlyList<string> AvailableFontFamilies { get; } = InitializeFontFamilies();

        private static List<string> InitializeFontFamilies()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();

            foreach (var f in PriorityFonts)
            {
                if (set.Add(f)) list.Add(f);
            }

            try
            {
                foreach (var font in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
                {
                    if (set.Add(font.Source))
                    {
                        list.Add(font.Source);
                    }
                }
            }
            catch { }

            return list;
        }

        public IReadOnlyList<ShapeType> AvailableShapeTypes { get; } = (ShapeType[])Enum.GetValues(typeof(ShapeType));
        public IReadOnlyList<StrokeStyle> AvailableStrokeStyles { get; } = (StrokeStyle[])Enum.GetValues(typeof(StrokeStyle));

        public bool HasSelection => SelectedItem != null;
        public bool HasSelectedOverlay => SelectedItem is OverlayItem;
        public bool IsVideoSelected => SelectedItem is VideoClip || SelectedItem is CutRange;
        public bool IsCutRangeSelected => SelectedItem is CutRange;
        public bool IsTextSelected => SelectedItem is TextOverlay;
        public bool IsShapeSelected => SelectedItem is ShapeOverlay;
        public bool IsImageSelected => SelectedItem is ImageOverlay;

        public bool IsExporting
        {
            get => _isExporting;
            set => SetProperty(ref _isExporting, value);
        }

        public double ExportProgress
        {
            get => _exportProgress;
            set => SetProperty(ref _exportProgress, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public double TimelineZoom
        {
            get => _timelineZoom;
            set
            {
                if (SetProperty(ref _timelineZoom, Math.Clamp(value, 0.1, 20.0)))
                {
                    OnPropertyChanged(nameof(TimelineZoomPercent));
                }
            }
        }

        public double TimelineZoomPercent
        {
            get => Math.Round(TimelineZoom * 100, 0);
            set => TimelineZoom = value / 100.0;
        }

        // Action invoked to notify view (e.g. MediaElement) to load media and seek
        public Action<string>? RequestLoadMedia { get; set; }
        public Action<double>? RequestMediaSeek { get; set; }
        public Action? RequestMediaPlay { get; set; }
        public Action? RequestMediaPause { get; set; }
        public Action<bool>? RequestScrubStateChange { get; set; }

        // Commands
        public ICommand OpenVideoCommand { get; }
        public ICommand PlayPauseCommand { get; }
        public ICommand StepForwardCommand { get; }
        public ICommand StepBackwardCommand { get; }
        public ICommand SeekCommand { get; }
        public ICommand SetInPointCommand { get; }
        public ICommand SetOutPointCommand { get; }
        public ICommand SplitCutCommand { get; }
        public ICommand ResetCutCommand { get; }
        public ICommand SelectVideoCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }
        public ICommand SetVolumePresetCommand { get; }
        public ICommand SetSpeedPresetCommand { get; }
        public ICommand SetOpacityPresetCommand { get; }
        public ICommand SetScalePresetCommand { get; }
        public ICommand SetRotationPresetCommand { get; }
        public ICommand SetTextAlignmentCommand { get; }
        public ICommand ApplyTextPresetColorCommand { get; }
        public ICommand SetShapeFillPresetCommand { get; }
        public ICommand SetShapeStrokePresetCommand { get; }
        public ICommand SetShapeTypeCommand { get; }
        public ICommand SetStrokeStyleCommand { get; }
        public ICommand AddTextOverlayCommand { get; }
        public ICommand AddShapeOverlayCommand { get; }
        public ICommand AddImageOverlayCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand DeleteSelectedOverlayCommand { get; }
        public ICommand DuplicateSelectedOverlayCommand { get; }
        public ICommand ExportLosslessCommand { get; }
        public ICommand ExportCompositeCommand { get; }
        public ICommand CancelExportCommand { get; }
        public ICommand SaveProjectCommand { get; }
        public ICommand LoadProjectCommand { get; }

        public MainViewModel()
        {
            _ffmpegService = new FFmpegService();

            _playbackTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };
            _playbackTimer.Tick += (s, e) =>
            {
                if (IsPlaying && !IsScrubbing)
                {
                    var now = DateTime.UtcNow;
                    double dt = (now - _lastPlaybackTick).TotalSeconds;
                    _lastPlaybackTick = now;

                    if (TotalDurationSeconds > 0)
                    {
                        double prev = CurrentTimeSeconds;
                        double next = prev + dt;
                        if (next >= TotalDurationSeconds)
                        {
                            Pause();
                            CurrentTimeSeconds = TotalDurationSeconds;
                        }
                        else
                        {
                            var prevClip = Project.CutRanges.FirstOrDefault(r => r.IsKeep && prev >= r.StartSeconds && prev < r.EndSeconds);
                            var nextClip = Project.CutRanges.FirstOrDefault(r => r.IsKeep && next >= r.StartSeconds && next < r.EndSeconds);

                            if (prevClip != nextClip)
                            {
                                CurrentTimeSeconds = next;
                            }
                            else
                            {
                                SetCurrentTimeInternal(next);
                            }
                        }
                    }
                }
            };

            SetupCollectionListeners(Project);

            OpenVideoCommand = new RelayCommand(async () => await OpenVideoDialogAsync());
            PlayPauseCommand = new RelayCommand(TogglePlayPause);
            StepForwardCommand = new RelayCommand(() => StepFrame(1));
            StepBackwardCommand = new RelayCommand(() => StepFrame(-1));
            SeekCommand = new RelayCommand<double?>(sec =>
            {
                if (sec.HasValue) SeekTo(sec.Value);
            });

            SetInPointCommand = new RelayCommand(SetInPoint);
            SetOutPointCommand = new RelayCommand(SetOutPoint);
            SplitCutCommand = new RelayCommand(SplitCutAtCurrentTime);
            ResetCutCommand = new RelayCommand(ResetCutRanges);
            SelectVideoCommand = new RelayCommand(() => SelectedItem = Project.SourceVideo);

            UndoCommand = new RelayCommand(Undo, () => CanUndo);
            RedoCommand = new RelayCommand(Redo, () => CanRedo);

            SetVolumePresetCommand = new RelayCommand<double?>(pct => { if (pct.HasValue) CurrentVolumePercent = pct.Value; });
            SetSpeedPresetCommand = new RelayCommand<double?>(pct => { if (pct.HasValue) CurrentPlaybackSpeedPercent = pct.Value; });
            SetOpacityPresetCommand = new RelayCommand<double?>(pct => { if (pct.HasValue) CurrentOpacityPercent = pct.Value; });
            SetScalePresetCommand = new RelayCommand<double?>(pct => { if (pct.HasValue) CurrentScalePercent = pct.Value; });
            SetRotationPresetCommand = new RelayCommand<double?>(deg => { if (deg.HasValue) CurrentRotationDegrees = deg.Value; });

            SetTextAlignmentCommand = new RelayCommand<string>(align =>
            {
                if (SelectedTextOverlay != null && !string.IsNullOrEmpty(align))
                {
                    RecordHistory();
                    SelectedTextOverlay.TextAlignment = align;
                }
            });

            ApplyTextPresetColorCommand = new RelayCommand<string>(preset =>
            {
                if (SelectedTextOverlay != null && !string.IsNullOrEmpty(preset))
                {
                    RecordHistory();
                    switch (preset)
                    {
                        case "WhiteBlack":
                            SelectedTextOverlay.TextColor = "#FFFFFF";
                            SelectedTextOverlay.OutlineColor = "#000000";
                            SelectedTextOverlay.OutlineThickness = 3;
                            break;
                        case "YellowBlack":
                            SelectedTextOverlay.TextColor = "#FFFF00";
                            SelectedTextOverlay.OutlineColor = "#000000";
                            SelectedTextOverlay.OutlineThickness = 3;
                            break;
                        case "RedWhite":
                            SelectedTextOverlay.TextColor = "#FF3333";
                            SelectedTextOverlay.OutlineColor = "#FFFFFF";
                            SelectedTextOverlay.OutlineThickness = 3;
                            break;
                        case "CyanBlack":
                            SelectedTextOverlay.TextColor = "#00E5FF";
                            SelectedTextOverlay.OutlineColor = "#000000";
                            SelectedTextOverlay.OutlineThickness = 3;
                            break;
                        case "GreenBlack":
                            SelectedTextOverlay.TextColor = "#00FF66";
                            SelectedTextOverlay.OutlineColor = "#000000";
                            SelectedTextOverlay.OutlineThickness = 3;
                            break;
                        case "BlackWhite":
                            SelectedTextOverlay.TextColor = "#000000";
                            SelectedTextOverlay.OutlineColor = "#FFFFFF";
                            SelectedTextOverlay.OutlineThickness = 3;
                            break;
                    }
                }
            });

            SetShapeFillPresetCommand = new RelayCommand<string>(color =>
            {
                if (SelectedShapeOverlay != null && !string.IsNullOrEmpty(color))
                {
                    RecordHistory();
                    SelectedShapeOverlay.FillColor = color;
                }
            });

            SetShapeStrokePresetCommand = new RelayCommand<string>(color =>
            {
                if (SelectedShapeOverlay != null && !string.IsNullOrEmpty(color))
                {
                    RecordHistory();
                    SelectedShapeOverlay.StrokeColor = color;
                }
            });

            SetShapeTypeCommand = new RelayCommand<ShapeType?>(type =>
            {
                if (SelectedShapeOverlay != null && type.HasValue)
                {
                    RecordHistory();
                    SelectedShapeOverlay.ShapeType = type.Value;
                }
            });

            SetStrokeStyleCommand = new RelayCommand<StrokeStyle?>(style =>
            {
                if (SelectedShapeOverlay != null && style.HasValue)
                {
                    RecordHistory();
                    SelectedShapeOverlay.StrokeStyle = style.Value;
                }
            });

            AddTextOverlayCommand = new RelayCommand(AddTextOverlay);
            AddShapeOverlayCommand = new RelayCommand<ShapeType?>(shape => AddShapeOverlay(shape ?? ShapeType.Rectangle));
            AddImageOverlayCommand = new RelayCommand(AddImageOverlay);
            DeleteSelectedCommand = new RelayCommand(DeleteSelected, () => HasSelection);
            DeleteSelectedOverlayCommand = new RelayCommand(DeleteSelected, () => HasSelection);
            DuplicateSelectedOverlayCommand = new RelayCommand(DuplicateSelectedOverlay, () => HasSelectedOverlay);

            ExportLosslessCommand = new RelayCommand(async () => await ExportLosslessAsync(), () => Project.SourceVideo != null && !IsExporting);
            ExportCompositeCommand = new RelayCommand(async () => await ExportCompositeAsync(), () => Project.SourceVideo != null && !IsExporting);
            CancelExportCommand = new RelayCommand(CancelExport, () => IsExporting);

            SaveProjectCommand = new RelayCommand(async () => await SaveProjectDialogAsync(), () => Project.SourceVideo != null);
            LoadProjectCommand = new RelayCommand(async () => await LoadProjectDialogAsync());
        }

        public async Task LoadVideoFileAsync(string filePath)
        {
            if (!File.Exists(filePath)) return;

            // Notify UI element (MediaElement) to load file immediately
            RequestLoadMedia?.Invoke(filePath);

            StatusMessage = "動画情報を読み込み中...";
            var clip = await _ffmpegService.ProbeVideoAsync(filePath);
            if (clip.DurationSeconds <= 0)
            {
                // Fallback: duration will be set when MediaElement opens
                clip.DurationSeconds = 10.0;
            }

            Project.Reset();
            Project.SourceVideo = clip;
            Project.CutRanges.Add(new CutRange(0, clip.DurationSeconds, true));

            CurrentTimeSeconds = 0;
            SelectedItem = clip; // Auto-select video clip YMM4-style
            OnPropertyChanged(nameof(TotalDurationSeconds));
            OnPropertyChanged(nameof(FormattedTotalTime));
            OnPropertyChanged(nameof(CurrentTimeDisplay));

            StatusMessage = $"動画読み込み完了: {clip.FileName} ({clip.Width}x{clip.Height}, {clip.Fps} fps, {FormatTime(clip.DurationSeconds)})";
            RequestMediaSeek?.Invoke(0);
        }

        public void NotifyMediaOpened(TimeSpan naturalDuration, int naturalWidth, int naturalHeight)
        {
            if (Project.SourceVideo != null)
            {
                if (naturalDuration.TotalSeconds > 0 && Math.Abs(Project.SourceVideo.DurationSeconds - naturalDuration.TotalSeconds) > 0.5)
                {
                    Project.SourceVideo.DurationSeconds = naturalDuration.TotalSeconds;
                    if (Project.CutRanges.Count == 1)
                    {
                        Project.CutRanges[0].EndSeconds = naturalDuration.TotalSeconds;
                    }
                }
                if (naturalWidth > 0) Project.SourceVideo.Width = naturalWidth;
                if (naturalHeight > 0) Project.SourceVideo.Height = naturalHeight;

                OnPropertyChanged(nameof(TotalDurationSeconds));
                OnPropertyChanged(nameof(FormattedTotalTime));
                OnPropertyChanged(nameof(CurrentTimeDisplay));
            }
        }

        public void TogglePlayPause()
        {
            if (IsPlaying) Pause();
            else Play();
        }

        public void Play()
        {
            if (Project.SourceVideo == null) return;
            if (CurrentTimeSeconds >= TotalDurationSeconds && TotalDurationSeconds > 0)
            {
                CurrentTimeSeconds = 0;
                RequestMediaSeek?.Invoke(0);
            }
            IsPlaying = true;
            RequestMediaPlay?.Invoke();
        }

        public void Pause()
        {
            IsPlaying = false;
            RequestMediaPause?.Invoke();
        }

        public void SeekTo(double seconds)
        {
            seconds = Math.Clamp(seconds, 0, TotalDurationSeconds);
            CurrentTimeSeconds = seconds;
            RequestMediaSeek?.Invoke(seconds);
        }

        public void StepFrame(int frameDelta)
        {
            Pause();
            double fps = Project.SourceVideo?.Fps ?? 30.0;
            if (fps <= 0) fps = 30.0;
            double step = frameDelta / fps;
            SeekTo(CurrentTimeSeconds + step);
        }

        private void SetupCollectionListeners(Project project)
        {
            project.CutRanges.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (CutRange r in e.NewItems)
                    {
                        r.PropertyChanged += (sender, args) =>
                        {
                            OnPropertyChanged(nameof(TotalDurationSeconds));
                            OnPropertyChanged(nameof(FormattedTotalTime));
                            OnPropertyChanged(nameof(CurrentTimeDisplay));
                        };
                    }
                }
                OnPropertyChanged(nameof(TotalDurationSeconds));
                OnPropertyChanged(nameof(FormattedTotalTime));
                OnPropertyChanged(nameof(CurrentTimeDisplay));
            };

            project.Overlays.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (OverlayItem o in e.NewItems)
                    {
                        o.PropertyChanged += (sender, args) =>
                        {
                            OnPropertyChanged(nameof(TotalDurationSeconds));
                            OnPropertyChanged(nameof(FormattedTotalTime));
                            OnPropertyChanged(nameof(CurrentTimeDisplay));
                        };
                    }
                }
                OnPropertyChanged(nameof(TotalDurationSeconds));
                OnPropertyChanged(nameof(FormattedTotalTime));
                OnPropertyChanged(nameof(CurrentTimeDisplay));
            };
        }

        public void SetInPoint()
        {
            if (Project.CutRanges.Count == 0) return;
            var currentRange = Project.CutRanges.FirstOrDefault(r => CurrentTimeSeconds >= r.StartSeconds && CurrentTimeSeconds <= r.EndSeconds);
            if (currentRange != null)
            {
                RecordHistory();
                double delta = CurrentTimeSeconds - currentRange.StartSeconds;
                currentRange.SourceStartSeconds = Math.Max(0, currentRange.SourceStartSeconds + delta);
                currentRange.StartSeconds = CurrentTimeSeconds;
                StatusMessage = $"イン点(開始位置)を設定: {FormatTime(CurrentTimeSeconds)}";
            }
        }

        public void SetOutPoint()
        {
            if (Project.CutRanges.Count == 0) return;
            var currentRange = Project.CutRanges.FirstOrDefault(r => CurrentTimeSeconds >= r.StartSeconds && CurrentTimeSeconds <= r.EndSeconds);
            if (currentRange != null)
            {
                RecordHistory();
                currentRange.EndSeconds = CurrentTimeSeconds;
                StatusMessage = $"アウト点(終了位置)を設定: {FormatTime(CurrentTimeSeconds)}";
            }
        }

        public void SplitCutAtCurrentTime()
        {
            if (Project.CutRanges.Count == 0) return;
            var target = Project.CutRanges.FirstOrDefault(r => CurrentTimeSeconds > r.StartSeconds + 0.05 && CurrentTimeSeconds < r.EndSeconds - 0.05);
            if (target != null)
            {
                RecordHistory();
                double oldEnd = target.EndSeconds;
                double splitOffset = CurrentTimeSeconds - target.StartSeconds;
                target.EndSeconds = CurrentTimeSeconds;

                var newRange = new CutRange(CurrentTimeSeconds, oldEnd, target.SourceStartSeconds + splitOffset, true)
                {
                    Volume = target.Volume,
                    PlaybackSpeed = target.PlaybackSpeed
                };
                int idx = Project.CutRanges.IndexOf(target);
                Project.CutRanges.Insert(idx + 1, newRange);
                SelectedItem = newRange;
                StatusMessage = $"タイムラインを分割しました: {FormatTime(CurrentTimeSeconds)}";
            }
        }

        public void ResetCutRanges()
        {
            if (Project.SourceVideo != null)
            {
                RecordHistory();
                Project.CutRanges.Clear();
                Project.CutRanges.Add(new CutRange(0, Project.SourceVideo.DurationSeconds, 0, true));
                StatusMessage = "カット範囲をリセットしました。";
            }
        }

        public void AddTextOverlay()
        {
            RecordHistory();
            double start = CurrentTimeSeconds;
            double end = Math.Min(TotalDurationSeconds, start + 3.0);
            if (end <= start) end = start + 3.0;

            int videoW = Project.SourceVideo?.Width ?? 1920;
            int videoH = Project.SourceVideo?.Height ?? 1080;

            var item = new TextOverlay
            {
                StartTime = start,
                EndTime = end,
                X = (videoW - 400) / 2.0,
                Y = videoH * 0.75, // Lower third default
                Width = 500,
                Height = 90,
                FontSize = 42,
                Text = "テキストを入力"
            };

            Project.Overlays.Add(item);
            SelectedOverlay = item;
            StatusMessage = "テキストを追加しました。画面上のテキストをドラッグして移動できます。";
        }

        public void AddShapeOverlay(ShapeType shapeType)
        {
            RecordHistory();
            double start = CurrentTimeSeconds;
            double end = Math.Min(TotalDurationSeconds, start + 3.0);
            if (end <= start) end = start + 3.0;

            int videoW = Project.SourceVideo?.Width ?? 1920;
            int videoH = Project.SourceVideo?.Height ?? 1080;

            var item = new ShapeOverlay
            {
                ShapeType = shapeType,
                Name = shapeType switch
                {
                    ShapeType.Rectangle => "四角形",
                    ShapeType.Ellipse => "円・楕円",
                    ShapeType.Arrow => "矢印",
                    _ => "直線"
                },
                StartTime = start,
                EndTime = end,
                X = (videoW - 300) / 2.0,
                Y = (videoH - 200) / 2.0,
                Width = shapeType == ShapeType.Arrow ? 240 : 200,
                Height = shapeType == ShapeType.Arrow ? 80 : 150,
                StrokeThickness = 5,
                StrokeColor = "#FF3366"
            };

            Project.Overlays.Add(item);
            SelectedOverlay = item;
            StatusMessage = $"{item.Name}を追加しました。ドラッグで位置やサイズを調整できます。";
        }

        public void AddImageOverlay()
        {
            var ofd = new OpenFileDialog
            {
                Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|すべてのファイル|*.*",
                Title = "配置する画像を選択"
            };
            if (ofd.ShowDialog() == true)
            {
                AddImageOverlayFromFile(ofd.FileName);
            }
        }

        public void AddImageOverlayFromFile(string imagePath)
        {
            if (!File.Exists(imagePath)) return;
            RecordHistory();

            double start = CurrentTimeSeconds;
            double end = Math.Min(TotalDurationSeconds, start + 3.0);
            if (end <= start) end = start + 3.0;

            int videoW = Project.SourceVideo?.Width ?? 1920;
            int videoH = Project.SourceVideo?.Height ?? 1080;

            var item = new ImageOverlay
            {
                Name = Path.GetFileNameWithoutExtension(imagePath),
                ImagePath = imagePath,
                StartTime = start,
                EndTime = end,
                X = (videoW - 250) / 2.0,
                Y = (videoH - 250) / 2.0,
                Width = 250,
                Height = 250
            };

            Project.Overlays.Add(item);
            SelectedOverlay = item;
            StatusMessage = $"画像を配置しました: {Path.GetFileName(imagePath)}";
        }

        public void DeleteSelected()
        {
            if (SelectedItem is OverlayItem item)
            {
                RecordHistory();
                Project.Overlays.Remove(item);
                SelectedItem = Project.SourceVideo;
                StatusMessage = "アイテムを削除しました。";
            }
            else if (SelectedItem is CutRange cr)
            {
                RecordHistory();
                Project.CutRanges.Remove(cr);
                SelectedItem = Project.CutRanges.FirstOrDefault() ?? (object?)Project.SourceVideo;
                StatusMessage = "カット区間を削除しました（無映像・無音区間になります）。";
                RequestMediaSeek?.Invoke(CurrentTimeSeconds);
            }
        }

        public void DeleteSelectedOverlay() => DeleteSelected();

        public void DuplicateSelectedOverlay()
        {
            if (SelectedOverlay != null)
            {
                RecordHistory();
                var clone = SelectedOverlay.Clone();
                Project.Overlays.Add(clone);
                SelectedOverlay = clone;
                StatusMessage = "アイテムを複製しました。";
            }
        }

        public async Task ExportLosslessAsync()
        {
            if (Project.SourceVideo == null) return;
            if (!await EnsureFFmpegReadyAsync()) return;

            var sfd = new SaveFileDialog
            {
                Filter = $"動画ファイル|*{Path.GetExtension(Project.SourceVideo.FilePath)}|すべてのファイル|*.*",
                FileName = $"{Path.GetFileNameWithoutExtension(Project.SourceVideo.FilePath)}_cut{Path.GetExtension(Project.SourceVideo.FilePath)}",
                Title = "無劣化カット書き出し先の保存"
            };

            if (sfd.ShowDialog() != true) return;

            IsExporting = true;
            ExportProgress = 0;
            StatusMessage = "⚡ 無劣化ストリームコピーで超高速書き出し中...";
            _exportCts = new CancellationTokenSource();

            try
            {
                var progress = new Progress<double>(p => ExportProgress = p * 100);
                var sw = Stopwatch.StartNew();
                await _ffmpegService.ExportLosslessCutAsync(Project.SourceVideo.FilePath, sfd.FileName, Project.CutRanges, progress, _exportCts.Token);
                sw.Stop();
                StatusMessage = $"✨ 無劣化書き出し完了！ 処理時間: {sw.Elapsed.TotalSeconds:F1}秒 ({sfd.FileName})";
                MessageBox.Show($"無劣化カット書き出しが完了しました！\n所要時間: {sw.Elapsed.TotalSeconds:F1}秒\n\n保存先: {sfd.FileName}", "書き出し完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "書き出しがキャンセルされました。";
            }
            catch (Exception ex)
            {
                StatusMessage = $"エラー: {ex.Message}";
                MessageBox.Show($"書き出しに失敗しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsExporting = false;
                _exportCts = null;
            }
        }

        public async Task ExportCompositeAsync()
        {
            if (Project.SourceVideo == null) return;
            if (!await EnsureFFmpegReadyAsync()) return;

            var sfd = new SaveFileDialog
            {
                Filter = "MP4 動画|*.mp4|MKV 動画|*.mkv|すべてのファイル|*.*",
                FileName = $"{Path.GetFileNameWithoutExtension(Project.SourceVideo.FilePath)}_edited.mp4",
                Title = "完全合成書き出し先の保存"
            };

            if (sfd.ShowDialog() != true) return;

            IsExporting = true;
            ExportProgress = 0;
            StatusMessage = "🎬 オーバーレイを合成して動画を書き出し中...";
            _exportCts = new CancellationTokenSource();

            try
            {
                var progress = new Progress<double>(p => ExportProgress = p * 100);
                var sw = Stopwatch.StartNew();

                await _ffmpegService.ExportCompositeAsync(
                    Project.SourceVideo.FilePath,
                    sfd.FileName,
                    Project.CutRanges,
                    Project.Overlays,
                    OverlayRenderer.RenderOverlayToTempPng,
                    Project.SourceVideo.Width,
                    Project.SourceVideo.Height,
                    progress,
                    _exportCts.Token);

                sw.Stop();
                StatusMessage = $"✨ 合成書き出し完了！ 処理時間: {sw.Elapsed.TotalSeconds:F1}秒 ({sfd.FileName})";
                MessageBox.Show($"動画の書き出しが完了しました！\n所要時間: {sw.Elapsed.TotalSeconds:F1}秒\n\n保存先: {sfd.FileName}", "書き出し完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "書き出しがキャンセルされました。";
            }
            catch (Exception ex)
            {
                StatusMessage = $"エラー: {ex.Message}";
                MessageBox.Show($"書き出しに失敗しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsExporting = false;
                _exportCts = null;
            }
        }

        public void CancelExport()
        {
            _exportCts?.Cancel();
        }

        private async Task<bool> EnsureFFmpegReadyAsync()
        {
            if (_ffmpegService.IsFFmpegAvailable) return true;

            var res = MessageBox.Show(
                "動画の書き出しには FFmpeg が必要です。\nFFmpeg を自動ダウンロードしますか？\n（[いいえ]を押すと手動で ffmpeg.exe を選択できます）",
                "FFmpeg の確認",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                IsExporting = true;
                ExportProgress = 0;
                StatusMessage = "FFmpeg をダウンロード中...";
                try
                {
                    var progress = new Progress<double>(p => ExportProgress = p * 100);
                    await _ffmpegService.DownloadFFmpegAsync(progress);
                    StatusMessage = "FFmpeg のセットアップが完了しました！";
                    return true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"FFmpeg のダウンロードに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                finally
                {
                    IsExporting = false;
                }
            }
            else if (res == MessageBoxResult.No)
            {
                var ofd = new OpenFileDialog
                {
                    Filter = "ffmpeg.exe|ffmpeg.exe|すべての実行ファイル|*.exe",
                    Title = "ffmpeg.exe を選択"
                };
                if (ofd.ShowDialog() == true)
                {
                    return true;
                }
            }

            return false;
        }

        private async Task OpenVideoDialogAsync()
        {
            var ofd = new OpenFileDialog
            {
                Filter = "動画ファイル|*.mp4;*.mkv;*.mov;*.avi;*.wmv;*.webm;*.flv;*.m4v;*.ts|すべてのファイル|*.*",
                Title = "動画ファイルを開く"
            };
            if (ofd.ShowDialog() == true)
            {
                await LoadVideoFileAsync(ofd.FileName);
            }
        }

        private async Task SaveProjectDialogAsync()
        {
            var sfd = new SaveFileDialog
            {
                Filter = "movieTweaks プロジェクト|*.mtproj",
                FileName = $"{Path.GetFileNameWithoutExtension(Project.SourceVideo?.FilePath ?? "project")}.mtproj",
                Title = "プロジェクトの保存"
            };
            if (sfd.ShowDialog() == true)
            {
                await ProjectSerializer.SaveAsync(Project, sfd.FileName);
                StatusMessage = $"プロジェクトを保存しました: {Path.GetFileName(sfd.FileName)}";
            }
        }

        private async Task LoadProjectDialogAsync()
        {
            var ofd = new OpenFileDialog
            {
                Filter = "movieTweaks プロジェクト|*.mtproj",
                Title = "プロジェクトを開く"
            };
            if (ofd.ShowDialog() == true)
            {
                var proj = await ProjectSerializer.LoadAsync(ofd.FileName);
                if (proj != null && proj.SourceVideo != null)
                {
                    Project = proj;
                    if (File.Exists(proj.SourceVideo.FilePath))
                    {
                        await LoadVideoFileAsync(proj.SourceVideo.FilePath);
                        // restore cut ranges & overlays
                        Project.CutRanges = proj.CutRanges;
                        Project.Overlays = proj.Overlays;
                        StatusMessage = $"プロジェクトを読み込みました: {Path.GetFileName(ofd.FileName)}";
                    }
                }
            }
        }

        public static string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 100:D1}";
        }
    }
}
