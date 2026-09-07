using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
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

        private Project _project = new();
        private double _currentTimeSeconds;
        private bool _isSyncingFromPlayer;
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
            set => SetProperty(ref _project, value);
        }

        public double CurrentTimeSeconds
        {
            get => _currentTimeSeconds;
            set
            {
                if (SetProperty(ref _currentTimeSeconds, Math.Max(0, value)))
                {
                    OnPropertyChanged(nameof(FormattedCurrentTime));
                    OnPropertyChanged(nameof(CurrentTimeDisplay));
                    if (!_isSyncingFromPlayer)
                    {
                        RequestMediaSeek?.Invoke(_currentTimeSeconds);
                    }
                }
            }
        }

        public void SyncCurrentTimeFromPlayer(double seconds)
        {
            _isSyncingFromPlayer = true;
            CurrentTimeSeconds = seconds;
            _isSyncingFromPlayer = false;

            if (IsPlaying)
            {
                CheckAndSkipCutRanges(seconds);
            }
        }

        private void CheckAndSkipCutRanges(double seconds)
        {
            if (Project.CutRanges.Count == 0) return;

            var validRanges = Project.CutRanges.Where(r => r.IsKeep && r.Duration > 0.05).OrderBy(r => r.StartSeconds).ToList();
            if (validRanges.Count == 0) return;

            bool isInside = validRanges.Any(r => seconds >= r.StartSeconds && seconds < r.EndSeconds);
            if (!isInside)
            {
                var nextRange = validRanges.FirstOrDefault(r => r.StartSeconds > seconds);
                if (nextRange != null)
                {
                    SeekTo(nextRange.StartSeconds);
                }
                else
                {
                    Pause();
                    CurrentTimeSeconds = validRanges.Last().EndSeconds;
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
            }
        }

        public double TotalDurationSeconds => Project.SourceVideo?.DurationSeconds ?? 0;

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
                    if (value) _playbackTimer.Start();
                    else _playbackTimer.Stop();
                }
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
            set => SetProperty(ref _timelineZoom, Math.Clamp(value, 0.2, 5.0));
        }

        // Action invoked to notify view (e.g. MediaElement) to load media and seek
        public Action<string>? RequestLoadMedia { get; set; }
        public Action<double>? RequestMediaSeek { get; set; }
        public Action? RequestMediaPlay { get; set; }
        public Action? RequestMediaPause { get; set; }

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
                Interval = TimeSpan.FromMilliseconds(33) // ~30 FPS UI tick
            };
            _playbackTimer.Tick += (s, e) =>
            {
                if (IsPlaying && TotalDurationSeconds > 0)
                {
                    if (CurrentTimeSeconds >= TotalDurationSeconds)
                    {
                        Pause();
                        CurrentTimeSeconds = TotalDurationSeconds;
                    }
                }
            };

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

        public void SetInPoint()
        {
            if (Project.CutRanges.Count == 0) return;
            var currentRange = Project.CutRanges.FirstOrDefault(r => CurrentTimeSeconds >= r.StartSeconds && CurrentTimeSeconds <= r.EndSeconds);
            if (currentRange != null)
            {
                RecordHistory();
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
                target.EndSeconds = CurrentTimeSeconds;

                var newRange = new CutRange(CurrentTimeSeconds, oldEnd, true);
                int idx = Project.CutRanges.IndexOf(target);
                Project.CutRanges.Insert(idx + 1, newRange);
                StatusMessage = $"タイムラインを分割しました: {FormatTime(CurrentTimeSeconds)}";
            }
        }

        public void ResetCutRanges()
        {
            if (Project.SourceVideo != null)
            {
                RecordHistory();
                Project.CutRanges.Clear();
                Project.CutRanges.Add(new CutRange(0, Project.SourceVideo.DurationSeconds, true));
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
            else if (SelectedItem is CutRange cr && Project.CutRanges.Count > 1)
            {
                RecordHistory();
                Project.CutRanges.Remove(cr);
                SelectedItem = Project.CutRanges.FirstOrDefault() ?? (object?)Project.SourceVideo;
                StatusMessage = "カット区間を削除しました。";
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
