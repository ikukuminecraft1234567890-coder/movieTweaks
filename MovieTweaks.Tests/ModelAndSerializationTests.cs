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
            Assert.Equal(1, project.Overlays.Count);

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
    }
}
