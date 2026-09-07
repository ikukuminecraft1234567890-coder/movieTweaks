using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MovieTweaks.ViewModels;

namespace MovieTweaks
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly DispatcherTimer _syncTimer;
        private bool _isSeekingByCode;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;

            // MediaElement Callbacks
            _vm.RequestLoadMedia = filePath =>
            {
                try
                {
                    Player.Stop();
                    Player.Source = new Uri(filePath, UriKind.Absolute);
                    // Trigger preroll / decode of first frame
                    Player.Play();
                    Player.Pause();
                }
                catch (Exception ex)
                {
                    _vm.StatusMessage = $"動画読み込み失敗: {ex.Message}";
                }
            };

            _vm.RequestMediaPlay = () =>
            {
                if (Player.Source != null)
                {
                    Player.Play();
                }
            };

            _vm.RequestMediaPause = () =>
            {
                if (Player.Source != null)
                {
                    Player.Pause();
                }
            };

            _vm.RequestMediaSeek = seconds =>
            {
                if (Player.Source != null)
                {
                    _isSeekingByCode = true;
                    Player.Position = TimeSpan.FromSeconds(seconds);
                    // In manual mode, scrubbing requires Play+Pause cycle if paused
                    if (!_vm.IsPlaying)
                    {
                        Player.Play();
                        Player.Pause();
                    }
                    _isSeekingByCode = false;
                }
            };

            // High-frequency sync timer for video playback position
            _syncTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };
            _syncTimer.Tick += SyncTimer_Tick;
            _syncTimer.Start();
        }

        private void SyncTimer_Tick(object? sender, EventArgs e)
        {
            if (_vm.IsPlaying && !_isSeekingByCode && Player.NaturalDuration.HasTimeSpan)
            {
                _vm.SyncCurrentTimeFromPlayer(Player.Position.TotalSeconds);
            }
        }

        private void Player_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (Player.NaturalDuration.HasTimeSpan)
            {
                _vm.NotifyMediaOpened(
                    Player.NaturalDuration.TimeSpan,
                    Player.NaturalVideoWidth,
                    Player.NaturalVideoHeight);

                // Ensure stopped at first frame
                Player.Position = TimeSpan.Zero;
                Player.Pause();

                UpdateVideoLayout();
            }
        }

        private void Player_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            _vm.StatusMessage = $"⚠️ 動画の再生に対応していません: {e.ErrorException.Message}";
            MessageBox.Show(
                $"動画のデコードに失敗しました:\n{e.ErrorException.Message}\n\n※ Windows のコーデックパックや H.264/AAC 形式の動画であることを確認してください。",
                "再生エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private void Player_MediaEnded(object sender, RoutedEventArgs e)
        {
            _vm.Pause();
            _vm.CurrentTimeSeconds = _vm.TotalDurationSeconds;
        }

        private void PreviewAreaGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateVideoLayout();
        }

        private void UpdateVideoLayout()
        {
            if (Player.NaturalVideoWidth <= 0 || Player.NaturalVideoHeight <= 0 ||
                PreviewAreaGrid.ActualWidth <= 0 || PreviewAreaGrid.ActualHeight <= 0)
            {
                return;
            }

            // Calculate actual rendered video dimensions with Uniform stretch
            double containerW = PreviewAreaGrid.ActualWidth;
            double containerH = PreviewAreaGrid.ActualHeight;
            double videoW = Player.NaturalVideoWidth;
            double videoH = Player.NaturalVideoHeight;

            double scale = Math.Min(containerW / videoW, containerH / videoH);
            double actualW = videoW * scale;
            double actualH = videoH * scale;

            Player.Width = actualW;
            Player.Height = actualH;
            OverlayCanvasControl.Width = actualW;
            OverlayCanvasControl.Height = actualH;
            OverlayCanvasControl.InvalidateVisuals();
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    var file = files[0];
                    var ext = Path.GetExtension(file).ToLowerInvariant();

                    if (ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".wmv" or ".webm" or ".flv" or ".m4v" or ".ts")
                    {
                        await _vm.LoadVideoFileAsync(file);
                    }
                    else if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif")
                    {
                        _vm.AddImageOverlayFromFile(file);
                    }
                }
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Ignore shortcut if user is currently typing in a TextBox
            if (e.OriginalSource is System.Windows.Controls.TextBox)
            {
                return;
            }

            if (e.Key == Key.Space)
            {
                _vm.TogglePlayPause();
                e.Handled = true;
            }
            else if (e.Key == Key.Left)
            {
                _vm.StepFrame(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -30 : -1);
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                _vm.StepFrame(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 30 : 1);
                e.Handled = true;
            }
            else if (e.Key == Key.I)
            {
                _vm.SetInPoint();
                e.Handled = true;
            }
            else if (e.Key == Key.O)
            {
                _vm.SetOutPoint();
                e.Handled = true;
            }
            else if (e.Key == Key.S)
            {
                _vm.SplitCutAtCurrentTime();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                if (_vm.HasSelectedOverlay)
                {
                    _vm.DeleteSelectedOverlay();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _vm.OpenVideoCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _vm.SaveProjectCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
