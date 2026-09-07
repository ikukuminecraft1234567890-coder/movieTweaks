namespace MovieTweaks.Models
{
    public class ImageOverlay : OverlayItem
    {
        private string _imagePath = string.Empty;
        private bool _keepAspectRatio = true;

        public string ImagePath
        {
            get => _imagePath;
            set => SetProperty(ref _imagePath, value);
        }

        public bool KeepAspectRatio
        {
            get => _keepAspectRatio;
            set => SetProperty(ref _keepAspectRatio, value);
        }

        public ImageOverlay()
        {
            Name = "画像";
            Width = 200;
            Height = 200;
        }

        public override OverlayItem Clone()
        {
            return new ImageOverlay
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
                ImagePath = ImagePath,
                KeepAspectRatio = KeepAspectRatio
            };
        }
    }
}
