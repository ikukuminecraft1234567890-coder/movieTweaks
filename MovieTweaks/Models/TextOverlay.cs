namespace MovieTweaks.Models
{
    public class TextOverlay : OverlayItem
    {
        private string _text = "テキストを入力";
        private string _fontFamily = "Yu Gothic UI";
        private double _fontSize = 48;
        private string _textColor = "#FFFFFF";
        private string _outlineColor = "#000000";
        private double _outlineThickness = 2;
        private string _backgroundColor = "Transparent";
        private double _backgroundCornerRadius = 0;
        private bool _isBold = true;
        private bool _isItalic;
        private string _textAlignment = "Center";

        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value);
        }

        public string FontFamily
        {
            get => _fontFamily;
            set => SetProperty(ref _fontFamily, value);
        }

        public double FontSize
        {
            get => _fontSize;
            set => SetProperty(ref _fontSize, value);
        }

        public string TextColor
        {
            get => _textColor;
            set => SetProperty(ref _textColor, value);
        }

        public string OutlineColor
        {
            get => _outlineColor;
            set => SetProperty(ref _outlineColor, value);
        }

        public double OutlineThickness
        {
            get => _outlineThickness;
            set => SetProperty(ref _outlineThickness, value);
        }

        public string BackgroundColor
        {
            get => _backgroundColor;
            set => SetProperty(ref _backgroundColor, value);
        }

        public double BackgroundCornerRadius
        {
            get => _backgroundCornerRadius;
            set => SetProperty(ref _backgroundCornerRadius, value);
        }

        public bool IsBold
        {
            get => _isBold;
            set => SetProperty(ref _isBold, value);
        }

        public bool IsItalic
        {
            get => _isItalic;
            set => SetProperty(ref _isItalic, value);
        }

        public string TextAlignment
        {
            get => _textAlignment;
            set => SetProperty(ref _textAlignment, value);
        }

        public TextOverlay()
        {
            Name = "テキスト";
            Width = 400;
            Height = 100;
        }

        public override OverlayItem Clone()
        {
            return new TextOverlay
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
                Text = Text,
                FontFamily = FontFamily,
                FontSize = FontSize,
                TextColor = TextColor,
                OutlineColor = OutlineColor,
                OutlineThickness = OutlineThickness,
                BackgroundColor = BackgroundColor,
                BackgroundCornerRadius = BackgroundCornerRadius,
                IsBold = IsBold,
                IsItalic = IsItalic,
                TextAlignment = TextAlignment
            };
        }
    }
}
