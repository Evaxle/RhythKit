using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace RhythKit.Models;

public class ColorItem : INotifyPropertyChanged
{
    private Color _color = Colors.White;
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; set; }

    public Color Color
    {
        get => _color;
        set
        {
            if (_color != value)
            {
                _color = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Hex));
                OnPropertyChanged(nameof(ColorBrush));
            }
        }
    }

    public string Hex => $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";

    public SolidColorBrush ColorBrush => new(_color);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
