using System;
using System.Text.Json.Serialization;
using MovieTweaks.ViewModels;

namespace MovieTweaks.Models
{
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(TextOverlay), "text")]
    [JsonDerivedType(typeof(ShapeOverlay), "shape")]
    [JsonDerivedType(typeof(ImageOverlay), "image")]
    public abstract class OverlayItem : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _name = "Item";
        private double _startTime;
        private double _endTime = 5.0;
        private double _x = 100;
        private double _y = 100;
        private double _width = 200;
        private double _height = 80;
        private double _rotation;
        private double _opacity = 1.0;
        private bool _isSelected;
        private int _zIndex;

        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public double StartTime
        {
            get => _startTime;
            set => SetProperty(ref _startTime, value);
        }

        public double EndTime
        {
            get => _endTime;
            set => SetProperty(ref _endTime, value);
        }

        public double Duration => Math.Max(0, EndTime - StartTime);

        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        public double Width
        {
            get => _width;
            set => SetProperty(ref _width, value);
        }

        public double Height
        {
            get => _height;
            set => SetProperty(ref _height, value);
        }

        public double Rotation
        {
            get => _rotation;
            set => SetProperty(ref _rotation, value);
        }

        public double Opacity
        {
            get => _opacity;
            set => SetProperty(ref _opacity, value);
        }

        [JsonIgnore]
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public int ZIndex
        {
            get => _zIndex;
            set => SetProperty(ref _zIndex, value);
        }

        public bool IsVisibleAt(double timeSeconds)
        {
            return timeSeconds >= StartTime && timeSeconds <= EndTime;
        }

        public abstract OverlayItem Clone();
    }
}
