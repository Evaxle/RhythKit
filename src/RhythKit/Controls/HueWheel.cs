using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using RhythKit.Services;

namespace RhythKit.Controls;

public class HueWheel : ContentControl
{
    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(nameof(SelectedColor), typeof(Color), typeof(HueWheel),
            new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedColorChanged));

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private const double WheelSize = 220;
    private const double Radius = 100;
    private WriteableBitmap? _wheelBitmap;
    private readonly Image _wheelImage;
    private readonly Ellipse _cursor;
    private readonly Canvas _root;
    private bool _isDragging;

    public static readonly DependencyProperty UseVProperty =
        DependencyProperty.Register(nameof(UseV), typeof(bool), typeof(HueWheel),
            new PropertyMetadata(false));

    public bool UseV
    {
        get => (bool)GetValue(UseVProperty);
        set => SetValue(UseVProperty, value);
    }

    public HueWheel()
    {
        _root = new Canvas { Width = WheelSize, Height = WheelSize };

        _wheelImage = new Image { Width = WheelSize, Height = WheelSize, Stretch = Stretch.Fill };
        _root.Children.Add(_wheelImage);

        _cursor = new Ellipse
        {
            Width = 14,
            Height = 14,
            Stroke = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            StrokeThickness = 2.5,
            Fill = new SolidColorBrush(Colors.Transparent),
            IsHitTestVisible = false
        };
        _root.Children.Add(_cursor);

        Content = _root;

        RenderWheel();
        UpdateCursorPosition();

        MouseLeftButtonDown += OnWheelMouseDown;
        MouseMove += OnWheelMouseMove;
        MouseLeftButtonUp += OnWheelMouseUp;
        SizeChanged += (_, _) => { RenderWheel(); UpdateCursorPosition(); };
    }

    private Point WheelCenter => new(WheelSize / 2, WheelSize / 2);

    private void RenderWheel()
    {
        _wheelBitmap = new WriteableBitmap((int)WheelSize, (int)WheelSize, 96, 96,
            PixelFormats.Pbgra32, null);
        _wheelImage.Source = _wheelBitmap;

        int w = (int)WheelSize;
        int h = (int)WheelSize;
        int stride = w * 4;
        var pixels = new byte[w * h * 4];

        double cx = WheelSize / 2;
        double cy = WheelSize / 2;
        double maxR = Radius;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double dx = x - cx;
                double dy = y - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                int idx = (y * w + x) * 4;

                if (dist > maxR)
                {
                    pixels[idx + 3] = 0;
                    continue;
                }

                if (dist < 3)
                {
                    pixels[idx] = 255;
                    pixels[idx + 1] = 255;
                    pixels[idx + 2] = 255;
                    pixels[idx + 3] = 255;
                    continue;
                }

                double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
                if (angle < 0) angle += 360;
                double hue = angle;
                double sat = dist / maxR;
                var color = ColorMath.FromHsv(hue, sat, 1.0);

                pixels[idx] = color.B;
                pixels[idx + 1] = color.G;
                pixels[idx + 2] = color.R;
                pixels[idx + 3] = 255;
            }
        }

        _wheelBitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
    }

    private void OnWheelMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        UpdateFromPosition(e.GetPosition(this));
        CaptureMouse();
    }

    private void OnWheelMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
            UpdateFromPosition(e.GetPosition(this));
    }

    private void OnWheelMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ReleaseMouseCapture();
    }

    private void UpdateFromPosition(Point pos)
    {
        var center = WheelCenter;
        double dx = pos.X - center.X;
        double dy = pos.Y - center.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > Radius)
            dist = Radius;
        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        if (angle < 0) angle += 360;
        double hue = angle;
        double sat = dist / Radius;
        SelectedColor = ColorMath.FromHsv(hue, sat, 1.0);
        UpdateCursorPosition();
    }

    private void UpdateCursorPosition()
    {
        var (h, s, _) = ColorMath.ToHsv(SelectedColor);
        double angle = h * Math.PI / 180;
        double radius = s * Radius;
        double cx = WheelSize / 2 + Math.Cos(angle) * radius;
        double cy = WheelSize / 2 + Math.Sin(angle) * radius;

        Canvas.SetLeft(_cursor, cx - _cursor.Width / 2);
        Canvas.SetTop(_cursor, cy - _cursor.Height / 2);
        _cursor.Fill = new SolidColorBrush(SelectedColor);
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HueWheel wheel)
            wheel.UpdateCursorPosition();
    }
}
