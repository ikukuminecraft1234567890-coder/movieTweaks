namespace MovieTweaks.Models
{
    public enum ShapeType
    {
        Rectangle,
        Ellipse,
        Arrow,
        Line
    }

    public class ShapeOverlay : OverlayItem
    {
        private ShapeType _shapeType = ShapeType.Rectangle;
        private string _strokeColor = "#FF3366";
        private string _fillColor = "Transparent";
        private double _strokeThickness = 4;
        private double _cornerRadius = 8;

        public ShapeType ShapeType
        {
            get => _shapeType;
            set => SetProperty(ref _shapeType, value);
        }

        public string StrokeColor
        {
            get => _strokeColor;
            set => SetProperty(ref _strokeColor, value);
        }

        public string FillColor
        {
            get => _fillColor;
            set => SetProperty(ref _fillColor, value);
        }

        public double StrokeThickness
        {
            get => _strokeThickness;
            set => SetProperty(ref _strokeThickness, value);
        }

        public double CornerRadius
        {
            get => _cornerRadius;
            set => SetProperty(ref _cornerRadius, value);
        }

        public ShapeOverlay()
        {
            Name = "図形";
            Width = 200;
            Height = 150;
        }

        public override OverlayItem Clone()
        {
            return new ShapeOverlay
            {
                Name = Name + " (コピー)",
                StartTime = StartTime,
                EndTime = EndTime,
                X = X + 20,
                Y = Y + 20,
                Width = Width,
                Height = Height,
                Rotation = Rotation,
                Opacity = Opacity,
                ShapeType = ShapeType,
                StrokeColor = StrokeColor,
                FillColor = FillColor,
                StrokeThickness = StrokeThickness,
                CornerRadius = CornerRadius
            };
        }
    }
}
