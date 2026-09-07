using System;
using MovieTweaks.ViewModels;

namespace MovieTweaks.Models
{
    public class CutRange : ObservableObject
    {
        private double _startSeconds;
        private double _endSeconds;
        private double _sourceStartSeconds;
        private double _volume = 1.0;
        private double _playbackSpeed = 1.0;
        private bool _isKeep = true;
        private bool _isSelected;

        public double StartSeconds
        {
            get => _startSeconds;
            set
            {
                if (SetProperty(ref _startSeconds, value))
                {
                    OnPropertyChanged(nameof(Duration));
                }
            }
        }

        public double EndSeconds
        {
            get => _endSeconds;
            set
            {
                if (SetProperty(ref _endSeconds, value))
                {
                    OnPropertyChanged(nameof(Duration));
                }
            }
        }

        public double SourceStartSeconds
        {
            get => _sourceStartSeconds;
            set => SetProperty(ref _sourceStartSeconds, Math.Max(0, value));
        }

        public double Duration => Math.Max(0, EndSeconds - StartSeconds);

        public double Volume
        {
            get => _volume;
            set => SetProperty(ref _volume, Math.Clamp(value, 0.0, 5.0));
        }

        public double VolumePercent
        {
            get => Math.Round(_volume * 100, 1);
            set
            {
                Volume = value / 100.0;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Volume));
            }
        }

        public double PlaybackSpeed
        {
            get => _playbackSpeed;
            set => SetProperty(ref _playbackSpeed, Math.Clamp(value, 0.1, 10.0));
        }

        public double PlaybackSpeedPercent
        {
            get => Math.Round(_playbackSpeed * 100, 1);
            set
            {
                PlaybackSpeed = value / 100.0;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlaybackSpeed));
            }
        }

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
            SourceStartSeconds = 0;
            IsKeep = isKeep;
        }

        public CutRange(double start, double end, double sourceStart, bool isKeep = true)
        {
            StartSeconds = start;
            EndSeconds = end;
            SourceStartSeconds = sourceStart;
            IsKeep = isKeep;
        }

        public CutRange Clone()
        {
            return new CutRange(StartSeconds, EndSeconds, SourceStartSeconds, IsKeep)
            {
                Volume = Volume,
                PlaybackSpeed = PlaybackSpeed,
                IsSelected = IsSelected
            };
        }
    }
}
