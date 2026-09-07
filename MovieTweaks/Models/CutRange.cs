using MovieTweaks.ViewModels;

namespace MovieTweaks.Models
{
    public class CutRange : ObservableObject
    {
        private double _startSeconds;
        private double _endSeconds;
        private bool _isKeep = true;
        private bool _isSelected;

        public double StartSeconds
        {
            get => _startSeconds;
            set => SetProperty(ref _startSeconds, value);
        }

        public double EndSeconds
        {
            get => _endSeconds;
            set => SetProperty(ref _endSeconds, value);
        }

        public double Duration => System.Math.Max(0, EndSeconds - StartSeconds);

        public bool IsKeep
        {
            get => _isKeep;
            set => SetProperty(ref _isKeep, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public CutRange() { }

        public CutRange(double start, double end, bool isKeep = true)
        {
            StartSeconds = start;
            EndSeconds = end;
            IsKeep = isKeep;
        }
    }
}
