using MovieTweaks.ViewModels;

namespace MovieTweaks.Models
{
    public class VideoClip : ObservableObject
    {
        private string _filePath = string.Empty;
        private double _durationSeconds;
        private int _width = 1920;
        private int _height = 1080;
        private double _fps = 30.0;
        private bool _hasAudio = true;
        private double _volume = 1.0;
        private double _playbackSpeed = 1.0;
        private double _opacity = 1.0;
        private bool _isSelected;

        public string FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }

        public string FileName => System.IO.Path.GetFileName(FilePath);

        public double DurationSeconds
        {
            get => _durationSeconds;
            set => SetProperty(ref _durationSeconds, value);
        }

        public int Width
        {
            get => _width;
            set => SetProperty(ref _width, value);
        }

        public int Height
        {
            get => _height;
            set => SetProperty(ref _height, value);
        }

        public double Fps
        {
            get => _fps;
            set => SetProperty(ref _fps, value);
        }

        public bool HasAudio
        {
            get => _hasAudio;
            set => SetProperty(ref _hasAudio, value);
        }

        public double Volume
        {
            get => _volume;
            set => SetProperty(ref _volume, Math.Clamp(value, 0.0, 1.0));
        }

        public double PlaybackSpeed
        {
            get => _playbackSpeed;
            set => SetProperty(ref _playbackSpeed, Math.Clamp(value, 0.25, 4.0));
        }

        public double Opacity
        {
            get => _opacity;
            set => SetProperty(ref _opacity, Math.Clamp(value, 0.0, 1.0));
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
