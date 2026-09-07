using System.Collections.ObjectModel;
using MovieTweaks.ViewModels;

namespace MovieTweaks.Models
{
    public class Project : ObservableObject
    {
        private VideoClip? _sourceVideo;
        private ObservableCollection<CutRange> _cutRanges = new();
        private ObservableCollection<OverlayItem> _overlays = new();

        public VideoClip? SourceVideo
        {
            get => _sourceVideo;
            set => SetProperty(ref _sourceVideo, value);
        }

        public ObservableCollection<CutRange> CutRanges
        {
            get => _cutRanges;
            set => SetProperty(ref _cutRanges, value);
        }

        public ObservableCollection<OverlayItem> Overlays
        {
            get => _overlays;
            set => SetProperty(ref _overlays, value);
        }

        public void Reset()
        {
            SourceVideo = null;
            CutRanges.Clear();
            Overlays.Clear();
        }
    }
}
