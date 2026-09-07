using System.IO;
using System.Threading.Tasks;
using MovieTweaks.Models;
using MovieTweaks.Services;
using MovieTweaks.ViewModels;
using Xunit;

namespace MovieTweaks.Tests
{
    public class ModelAndSerializationTests
    {
        [Fact]
        public void CutRange_Duration_CalculatesCorrectly()
        {
            var range = new CutRange(2.5, 10.5, true);
            Assert.Equal(8.0, range.Duration);
            Assert.True(range.IsKeep);
        }

        [Fact]
        public void TextOverlay_Clone_CreatesExactCopy()
        {
            var original = new TextOverlay
            {
                Text = "タイトル",
                FontSize = 52,
                StartTime = 1.0,
                EndTime = 4.5,
                TextColor = "#FF0000",
                OutlineColor = "#000000",
                OutlineThickness = 3,
                X = 150,
                Y = 300
            };

            var clone = (TextOverlay)original.Clone();
            Assert.Equal(original.Text, clone.Text);
            Assert.Equal(original.FontSize, clone.FontSize);
            Assert.Equal(original.TextColor, clone.TextColor);
            Assert.Equal(original.StartTime, clone.StartTime);
            Assert.Equal(original.EndTime, clone.EndTime);
            Assert.Contains("コピー", clone.Name);
        }

        [Fact]
        public void Overlay_IsVisibleAt_ReturnsTrueWithinRange()
        {
            var overlay = new TextOverlay
            {
                StartTime = 2.0,
                EndTime = 5.0
            };

            Assert.False(overlay.IsVisibleAt(1.9));
            Assert.True(overlay.IsVisibleAt(2.0));
            Assert.True(overlay.IsVisibleAt(3.5));
            Assert.True(overlay.IsVisibleAt(5.0));
            Assert.False(overlay.IsVisibleAt(5.1));
        }

        [Fact]
        public async Task ProjectSerializer_SaveAndLoad_RoundTripSuccess()
        {
            var project = new Project
            {
                SourceVideo = new VideoClip
                {
                    FilePath = @"C:\Videos\sample.mp4",
                    DurationSeconds = 120.5,
                    Width = 1920,
                    Height = 1080,
                    Fps = 60.0
                }
            };

            project.CutRanges.Add(new CutRange(0, 30, true));
            project.CutRanges.Add(new CutRange(40, 90, true));

            project.Overlays.Add(new TextOverlay
            {
                Text = "テストテキスト",
                FontSize = 36,
                StartTime = 5.0,
                EndTime = 10.0
            });

            project.Overlays.Add(new ShapeOverlay
            {
                ShapeType = ShapeType.Arrow,
                StartTime = 6.0,
                EndTime = 9.0
            });

            var tempFile = Path.Combine(Path.GetTempPath(), $"test_proj_{System.Guid.NewGuid():N}.mtproj");
            try
            {
                await ProjectSerializer.SaveAsync(project, tempFile);
                var loaded = await ProjectSerializer.LoadAsync(tempFile);

                Assert.NotNull(loaded);
                Assert.NotNull(loaded.SourceVideo);
                Assert.Equal(project.SourceVideo.FilePath, loaded.SourceVideo.FilePath);
                Assert.Equal(2, loaded.CutRanges.Count);
                Assert.Equal(2, loaded.Overlays.Count);
                Assert.IsType<TextOverlay>(loaded.Overlays[0]);
                Assert.IsType<ShapeOverlay>(loaded.Overlays[1]);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void MainViewModel_FormatTime_FormatsProperly()
        {
            Assert.Equal("00:00.0", MainViewModel.FormatTime(0));
            Assert.Equal("00:05.5", MainViewModel.FormatTime(5.5));
            Assert.Equal("01:23.4", MainViewModel.FormatTime(83.45));
            Assert.Equal("10:00.0", MainViewModel.FormatTime(600));
        }

        [Fact]
        public void UndoRedoService_UndoAndRedo_WorksCorrectly()
        {
            var project = new Project
            {
                SourceVideo = new VideoClip { FilePath = "test.mp4", DurationSeconds = 60 }
            };
            project.CutRanges.Add(new CutRange(0, 60, true));

            var undoRedo = new UndoRedoService();
            Assert.False(undoRedo.CanUndo);
            Assert.False(undoRedo.CanRedo);

            // Step 1: Record initial state and add an overlay
            undoRedo.RecordState(project);
            project.Overlays.Add(new TextOverlay { Text = "First Item" });
            Assert.True(undoRedo.CanUndo);
            Assert.Single(project.Overlays);

            // Step 2: Undo
            bool undone = undoRedo.Undo(project);
            Assert.True(undone);
            Assert.Empty(project.Overlays);
            Assert.True(undoRedo.CanRedo);

            // Step 3: Redo
            bool redone = undoRedo.Redo(project);
            Assert.True(redone);
            Assert.Single(project.Overlays);
            Assert.Equal("First Item", ((TextOverlay)project.Overlays[0]).Text);
        }

        [Fact]
        public void CutRange_SourceStartSeconds_CalculatesAndClonesCorrectly()
        {
            var cr = new CutRange(10.0, 25.0, 5.0, true)
            {
                Volume = 0.8,
                PlaybackSpeed = 1.5
            };

            Assert.Equal(15.0, cr.Duration);
            Assert.Equal(5.0, cr.SourceStartSeconds);
            Assert.Equal(0.8, cr.Volume);
            Assert.Equal(1.5, cr.PlaybackSpeed);

            var cloned = cr.Clone();
            Assert.Equal(cr.StartSeconds, cloned.StartSeconds);
            Assert.Equal(cr.EndSeconds, cloned.EndSeconds);
            Assert.Equal(cr.SourceStartSeconds, cloned.SourceStartSeconds);
            Assert.Equal(cr.Volume, cloned.Volume);
            Assert.Equal(cr.PlaybackSpeed, cloned.PlaybackSpeed);
        }

        [Fact]
        public void MainViewModel_SplitCutAtCurrentTime_MaintainsAccurateSourceOffsets()
        {
            var vm = new MainViewModel();
            vm.Project.SourceVideo = new VideoClip { DurationSeconds = 100 };
            vm.Project.CutRanges.Clear();
            vm.Project.CutRanges.Add(new CutRange(0, 100, 0, true));

            // Split at t = 30
            vm.CurrentTimeSeconds = 30;
            vm.SplitCutAtCurrentTime();

            Assert.Equal(2, vm.Project.CutRanges.Count);
            Assert.Equal(0, vm.Project.CutRanges[0].StartSeconds);
            Assert.Equal(30, vm.Project.CutRanges[0].EndSeconds);
            Assert.Equal(0, vm.Project.CutRanges[0].SourceStartSeconds);

            Assert.Equal(30, vm.Project.CutRanges[1].StartSeconds);
            Assert.Equal(100, vm.Project.CutRanges[1].EndSeconds);
            Assert.Equal(30, vm.Project.CutRanges[1].SourceStartSeconds);

            // Split 2nd clip at t = 50
            vm.CurrentTimeSeconds = 50;
            vm.SplitCutAtCurrentTime();

            Assert.Equal(3, vm.Project.CutRanges.Count);
            Assert.Equal(30, vm.Project.CutRanges[1].StartSeconds);
            Assert.Equal(50, vm.Project.CutRanges[1].EndSeconds);
            Assert.Equal(30, vm.Project.CutRanges[1].SourceStartSeconds);

            Assert.Equal(50, vm.Project.CutRanges[2].StartSeconds);
            Assert.Equal(100, vm.Project.CutRanges[2].EndSeconds);
            Assert.Equal(50, vm.Project.CutRanges[2].SourceStartSeconds);
        }

        [Fact]
        public void PercentConverter_ConvertsBidirectionally_AllowsValuesAbove100()
        {
            var conv = MovieTweaks.Controls.PercentConverter.Instance;

            // Convert to string
            Assert.Equal("150%", conv.Convert(150.0, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal("200%", conv.Convert(200, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));

            // ConvertBack from string
            Assert.Equal(150.0, conv.ConvertBack("150%", typeof(double), null, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(250.0, conv.ConvertBack("250", typeof(double), null, System.Globalization.CultureInfo.InvariantCulture));
        }

        [Fact]
        public void UnitConverter_ConvertsBidirectionally()
        {
            var conv = MovieTweaks.Controls.UnitConverter.Instance;

            Assert.Equal("48 pt", conv.Convert(48.0, typeof(string), "pt", System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(48.0, conv.ConvertBack("48 pt", typeof(double), "pt", System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(12.0, conv.ConvertBack("12px", typeof(double), "px", System.Globalization.CultureInfo.InvariantCulture));
        }

        [Fact]
        public void VolumeAndSpeedPercent_AllowsValuesAbove100Percent()
        {
            var clip = new CutRange(0, 10, 0, true);

            // Volume > 100%
            clip.VolumePercent = 250;
            Assert.Equal(2.5, clip.Volume);
            Assert.Equal(250.0, clip.VolumePercent);

            // PlaybackSpeed > 100%
            clip.PlaybackSpeedPercent = 200;
            Assert.Equal(2.0, clip.PlaybackSpeed);
            Assert.Equal(200.0, clip.PlaybackSpeedPercent);
        }

        [Fact]
        public void MainViewModel_SetClipPlaybackSpeed_RecalculatesTimelineDurationAndRipples()
        {
            var vm = new MainViewModel();
            vm.Project.SourceVideo = new VideoClip { DurationSeconds = 20 };
            vm.Project.CutRanges.Clear();

            // 2 clips: [0, 10] and [10, 20]
            var clip1 = new CutRange(0, 10, 0, true) { PlaybackSpeed = 1.0 };
            var clip2 = new CutRange(10, 20, 10, true) { PlaybackSpeed = 1.0 };
            vm.Project.CutRanges.Add(clip1);
            vm.Project.CutRanges.Add(clip2);

            // Double clip1 speed to 2.0 (200%): 10s source / 2.0 = 5s on timeline
            vm.SetClipPlaybackSpeed(clip1, 2.0);

            Assert.Equal(0, clip1.StartSeconds);
            Assert.Equal(5.0, clip1.EndSeconds);
            Assert.Equal(2.0, clip1.PlaybackSpeed);

            // clip2 should ripple shift from 10 to 5
            Assert.Equal(5.0, clip2.StartSeconds);
            Assert.Equal(15.0, clip2.EndSeconds);
        }

        [Fact]
        public void RelayCommand_ConvertsStringParameterToDouble()
        {
            double received = 0;
            var cmd = new RelayCommand<double?>(val => { if (val.HasValue) received = val.Value; });

            cmd.Execute("150");
            Assert.Equal(150.0, received);
        }

        [Fact]
        public void VolumeAndSpeed_UncappedBeyond500Percent_Supports50xPlaybackSpeed()
        {
            var clip = new CutRange(0, 100, 0, true);

            // Volume up to 2000% (20.0)
            clip.VolumePercent = 2000.0;
            Assert.Equal(20.0, clip.Volume);
            Assert.Equal(2000.0, clip.VolumePercent);

            // Speed up to 50x = 5000% (50.0)
            clip.PlaybackSpeedPercent = 5000.0;
            Assert.Equal(50.0, clip.PlaybackSpeed);
            Assert.Equal(5000.0, clip.PlaybackSpeedPercent);

            // VideoClip test
            var video = new VideoClip { FilePath = "sample.mp4" };
            video.PlaybackSpeedPercent = 5000.0;
            Assert.Equal(50.0, video.PlaybackSpeed);

            video.VolumePercent = 1500.0;
            Assert.Equal(15.0, video.Volume);
        }

        [Fact]
        public void FFmpegService_BuildAtempoFilter_ChainsCorrectlyForHighSpeed()
        {
            // 50x speed: 2^5 = 32, 50 / 32 = 1.5625
            string atempo = FFmpegService.BuildAtempoFilter(50.0);
            Assert.Contains("atempo=2.0", atempo);
            var parts = atempo.Split(',');
            Assert.Equal(6, parts.Length); // 5 x atempo=2.0 + 1 x atempo=1.562
            for (int i = 0; i < 5; i++)
            {
                Assert.Equal("atempo=2.0", parts[i]);
            }
            Assert.Equal("atempo=1.562", parts[5]);
        }

        [Fact]
        public void ShapeOverlay_NewShapes_StarHeartTriangle_ClonesAndSerializesCorrectly()
        {
            var shape = new ShapeOverlay
            {
                ShapeType = ShapeType.Star,
                StrokeStyle = StrokeStyle.Dash,
                StrokeColor = "#FF5500",
                FillColor = "#FFFF00",
                StrokeThickness = 5,
                Rotation = 45.0,
                CornerRadius = 12.0
            };

            var cloned = (ShapeOverlay)shape.Clone();
            Assert.Equal(ShapeType.Star, cloned.ShapeType);
            Assert.Equal(StrokeStyle.Dash, cloned.StrokeStyle);
            Assert.Equal("#FF5500", cloned.StrokeColor);
            Assert.Equal("#FFFF00", cloned.FillColor);
            Assert.Equal(5, cloned.StrokeThickness);
            Assert.Equal(45.0, cloned.Rotation);
            Assert.Equal(12.0, cloned.CornerRadius);

            // Test SpeechBubble and Heart
            var bubble = new ShapeOverlay { ShapeType = ShapeType.SpeechBubble, StrokeStyle = StrokeStyle.Dot };
            var bubbleClone = (ShapeOverlay)bubble.Clone();
            Assert.Equal(ShapeType.SpeechBubble, bubbleClone.ShapeType);
            Assert.Equal(StrokeStyle.Dot, bubbleClone.StrokeStyle);
        }

        [Fact]
        public void TextOverlay_FontFamilyAndAlignment_ClonesAndSerializesCorrectly()
        {
            var text = new TextOverlay
            {
                Text = "カスタムフォントテロップ",
                FontFamily = "Impact",
                FontSize = 64,
                IsBold = true,
                IsItalic = true,
                TextAlignment = "Right",
                Rotation = 15.0
            };

            var cloned = (TextOverlay)text.Clone();
            Assert.Equal("Impact", cloned.FontFamily);
            Assert.True(cloned.IsBold);
            Assert.True(cloned.IsItalic);
            Assert.Equal("Right", cloned.TextAlignment);
            Assert.Equal(15.0, cloned.Rotation);
        }

        [Fact]
        public void Converters_ShapeTypeAndStrokeStyle_ProvideJapaneseDescriptions()
        {
            var shapeConv = MovieTweaks.Controls.ShapeTypeConverter.Instance;
            var strokeConv = MovieTweaks.Controls.StrokeStyleConverter.Instance;

            var culture = System.Globalization.CultureInfo.InvariantCulture;
            Assert.Contains("星", shapeConv.Convert(ShapeType.Star, typeof(string), null, culture)?.ToString());
            Assert.Contains("ハート", shapeConv.Convert(ShapeType.Heart, typeof(string), null, culture)?.ToString());
            Assert.Contains("吹き出し", shapeConv.Convert(ShapeType.SpeechBubble, typeof(string), null, culture)?.ToString());
            Assert.Contains("三角形", shapeConv.Convert(ShapeType.Triangle, typeof(string), null, culture)?.ToString());

            Assert.Contains("破線", strokeConv.Convert(StrokeStyle.Dash, typeof(string), null, culture)?.ToString());
            Assert.Contains("点線", strokeConv.Convert(StrokeStyle.Dot, typeof(string), null, culture)?.ToString());
            Assert.Contains("実線", strokeConv.Convert(StrokeStyle.Solid, typeof(string), null, culture)?.ToString());
        }

        [Fact]
        public void MainViewModel_SetClipPlaybackSpeed_SupportsHighSpeed5xAnd50x()
        {
            var vm = new MainViewModel();
            vm.Project.SourceVideo = new VideoClip { DurationSeconds = 100 };
            vm.Project.CutRanges.Clear();

            var clip = new CutRange(0, 50, 0, true) { PlaybackSpeed = 1.0 };
            vm.Project.CutRanges.Add(clip);

            // 5x speed (500%): 50s source / 5.0 = 10s on timeline
            vm.SetClipPlaybackSpeed(clip, 5.0);
            Assert.Equal(10.0, clip.Duration);
            Assert.Equal(5.0, clip.PlaybackSpeed);
            Assert.Equal(500.0, clip.PlaybackSpeedPercent);

            // 50x speed (5000%): 50s source / 50.0 = 1.0s on timeline
            vm.SetClipPlaybackSpeed(clip, 50.0);
            Assert.Equal(1.0, clip.Duration);
            Assert.Equal(50.0, clip.PlaybackSpeed);
            Assert.Equal(5000.0, clip.PlaybackSpeedPercent);
        }

        [Fact]
        public void MainViewModel_Scrubbing_DuringPlayback_PausesAndResumesPlaybackAutomatically()
        {
            var vm = new MainViewModel();
            vm.Project.SourceVideo = new VideoClip { DurationSeconds = 30 };
            vm.Project.CutRanges.Clear();
            vm.Project.CutRanges.Add(new CutRange(0, 30, 0, true));

            bool scrubCallbackState = false;
            int scrubCallbackCount = 0;
            vm.RequestScrubStateChange = state =>
            {
                scrubCallbackState = state;
                scrubCallbackCount++;
            };

            // Start playing
            vm.IsPlaying = true;
            Assert.True(vm.IsPlaying);

            // Start scrubbing
            vm.IsScrubbing = true;
            Assert.True(vm.IsScrubbing);
            Assert.True(scrubCallbackState);
            Assert.Equal(1, scrubCallbackCount);

            // Finish scrubbing -> should automatically resume playback!
            vm.IsScrubbing = false;
            Assert.False(vm.IsScrubbing);
            Assert.False(scrubCallbackState);
            Assert.Equal(2, scrubCallbackCount);
            Assert.True(vm.IsPlaying);
        }

        [Fact]
        public void MainViewModel_SetCurrentTimeInternal_UpdatesDisplayWithoutTriggeringSeek()
        {
            var vm = new MainViewModel();
            vm.Project.SourceVideo = new VideoClip { DurationSeconds = 60 };
            vm.Project.CutRanges.Clear();
            vm.Project.CutRanges.Add(new CutRange(0, 60, 0, true));

            int seekCallCount = 0;
            double lastSeekTime = -1;
            vm.RequestMediaSeek = time =>
            {
                seekCallCount++;
                lastSeekTime = time;
            };

            // Calling SetCurrentTimeInternal (as used during continuous in-clip playback)
            vm.SetCurrentTimeInternal(15.5);
            Assert.Equal(15.5, vm.CurrentTimeSeconds);
            Assert.Contains("00:15.5", vm.CurrentTimeDisplay);
            // Must NOT have called RequestMediaSeek!
            Assert.Equal(0, seekCallCount);

            // Calling CurrentTimeSeconds setter (as used by explicit seek/cut boundary jump)
            vm.CurrentTimeSeconds = 25.0;
            Assert.Equal(25.0, vm.CurrentTimeSeconds);
            Assert.Equal(1, seekCallCount);
            Assert.Equal(25.0, lastSeekTime);
        }
    }
}
