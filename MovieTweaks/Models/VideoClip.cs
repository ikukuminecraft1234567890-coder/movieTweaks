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
    }
}
