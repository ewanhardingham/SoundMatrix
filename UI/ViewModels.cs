using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace SoundMatrix;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class DeviceVM : Observable
{
    public DeviceVM(string id)
    {
        Id = id;
        Apps.CollectionChanged += (_, _) => { Raise(nameof(Subtitle)); Raise(nameof(HasApps)); };
    }

    public string Id { get; }
    public ObservableCollection<AppVM> Apps { get; } = [];
    public bool HasApps => Apps.Count > 0;

    string _name = "";
    public string Name { get => _name; set => Set(ref _name, value); }

    int _number;
    public int Number { get => _number; set { if (Set(ref _number, value)) Raise(nameof(NumberText)); } }
    public string NumberText => Number is >= 1 and <= 9 ? Number.ToString() : "·";

    bool _isDefault;
    public bool IsDefault { get => _isDefault; set { if (Set(ref _isDefault, value)) Raise(nameof(Subtitle)); } }

    public string Subtitle => (IsDefault ? "Default device  ·  " : "") + (Apps.Count == 1 ? "1 app" : $"{Apps.Count} apps");

    /// <summary>True while an app from another device is picked up and this device can receive it.</summary>
    bool _isTarget;
    public bool IsTarget { get => _isTarget; set => Set(ref _isTarget, value); }

    Color _color;
    public Color Color
    {
        get => _color;
        set
        {
            if (!Set(ref _color, value)) return;
            AccentBrush = Freeze(new SolidColorBrush(value));
            PanelBrush = Freeze(new LinearGradientBrush(WithAlpha(value, 0x3A), WithAlpha(value, 0x12), new Point(0, 0), new Point(1, 1)));
            TileBrush = Freeze(new LinearGradientBrush(WithAlpha(value, 0x5C), WithAlpha(value, 0x1E), new Point(0, 0), new Point(0.3, 1)));
            EdgeBrush = Freeze(new LinearGradientBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF), WithAlpha(value, 0x50), new Point(0, 0), new Point(0, 1)));
            Raise(nameof(AccentBrush)); Raise(nameof(PanelBrush)); Raise(nameof(TileBrush)); Raise(nameof(EdgeBrush));
        }
    }

    public Brush AccentBrush { get; private set; } = Brushes.White;
    public Brush PanelBrush { get; private set; } = Brushes.Transparent;
    public Brush TileBrush { get; private set; } = Brushes.Transparent;
    public Brush EdgeBrush { get; private set; } = Brushes.Transparent;

    static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    static Brush Freeze(Brush b) { b.Freeze(); return b; }
}

public sealed class AppVM : Observable
{
    public AppVM(string key) => Key = key;

    public string Key { get; }

    string _name = "";
    public string Name { get => _name; set => Set(ref _name, value); }

    ImageSource? _icon;
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }

    double _volume;
    public double Volume { get => _volume; set { if (Set(ref _volume, value)) Raise(nameof(VolumeText)); } }

    bool _isMuted;
    public bool IsMuted { get => _isMuted; set { if (Set(ref _isMuted, value)) Raise(nameof(VolumeText)); } }

    public string VolumeText => $"{Math.Round(Volume * 100)}%";

    double _peak;
    public double Peak { get => _peak; set => Set(ref _peak, value); }

    bool _isFocused;
    public bool IsFocused { get => _isFocused; set { if (Set(ref _isFocused, value)) Raise(nameof(Scale)); } }

    bool _isHeld;
    public bool IsHeld { get => _isHeld; set { if (Set(ref _isHeld, value)) Raise(nameof(Scale)); } }

    public double Scale => IsHeld ? 1.08 : IsFocused ? 1.04 : 1.0;

    DeviceVM? _device;
    public DeviceVM? Device { get => _device; set => Set(ref _device, value); }
}

public sealed record HintVM(string Keys, string Label);
