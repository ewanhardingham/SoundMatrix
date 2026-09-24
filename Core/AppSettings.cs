using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace SoundMatrix;

public enum HotkeyAction
{
    ToggleOverlay,
    OpenSettings,
    NavigateUp,
    NavigateDown,
    NavigateLeft,
    NavigateRight,
    PickUp,
    ToggleMute,
    VolumeUp,
    VolumeDown,
    Close,
}

public sealed class AppSettings
{
    public static readonly IReadOnlyDictionary<HotkeyAction, string> DefaultHotkeys = new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.ToggleOverlay] = "Ctrl+Alt+S",
        [HotkeyAction.OpenSettings] = "Ctrl+OemComma",
        [HotkeyAction.NavigateUp] = "Up",
        [HotkeyAction.NavigateDown] = "Down",
        [HotkeyAction.NavigateLeft] = "Left",
        [HotkeyAction.NavigateRight] = "Right",
        [HotkeyAction.PickUp] = "Return",
        [HotkeyAction.ToggleMute] = "Space",
        [HotkeyAction.VolumeUp] = "OemPlus",
        [HotkeyAction.VolumeDown] = "OemMinus",
        [HotkeyAction.Close] = "Escape",
    };

    /// <summary>Distinct, glass-friendly accents. Devices get the next unused one.</summary>
    public static readonly string[] Palette =
    [
        "#22D3EE", // cyan
        "#A78BFA", // violet
        "#FBBF24", // amber
        "#FB7185", // rose
        "#34D399", // emerald
        "#60A5FA", // blue
        "#FB923C", // orange
        "#F472B6", // pink
        "#A3E635", // lime
    ];

    public const int DefaultVolumeStep = 5;
    public const double DefaultBackdropDim = 0.45;

    public Dictionary<HotkeyAction, string> Hotkeys { get; set; } = new(DefaultHotkeys);
    public int VolumeStep { get; set; } = DefaultVolumeStep;
    public List<string> HiddenDevices { get; set; } = [];
    public List<string> DeviceOrder { get; set; } = [];
    public Dictionary<string, string> DeviceColors { get; set; } = [];
    public double BackdropDim { get; set; } = DefaultBackdropDim;
    public bool BlurBackdrop { get; set; } = true;

    public Hotkey Get(HotkeyAction action) =>
        Hotkey.Parse(Hotkeys.TryGetValue(action, out var s) ? s : DefaultHotkeys[action]);

    public void Set(HotkeyAction action, Hotkey hotkey) => Hotkeys[action] = hotkey.Serialize();

    public bool IsHidden(string deviceId) => HiddenDevices.Contains(deviceId);

    /// <summary>Assigns a palette colour to a device the first time it's seen. Returns true if settings changed.</summary>
    public bool EnsureColor(string deviceId)
    {
        if (DeviceColors.ContainsKey(deviceId)) return false;
        var used = DeviceColors.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        DeviceColors[deviceId] = Palette.FirstOrDefault(p => !used.Contains(p)) ?? Palette[DeviceColors.Count % Palette.Length];
        return true;
    }

    public Color ColorFor(string deviceId)
    {
        EnsureColor(deviceId);
        return ParseColor(DeviceColors[deviceId]);
    }

    public static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return (Color)ColorConverter.ConvertFromString(Palette[0]); }
    }

    /// <summary>Orders devices by the user's saved order; unknown devices keep enumeration order at the end.</summary>
    public List<T> Order<T>(IEnumerable<T> items, Func<T, string> id) =>
        items.OrderBy(x => DeviceOrder.IndexOf(id(x)) is var i and >= 0 ? i : int.MaxValue).ToList();

    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SoundMatrix");
    static string FilePath => Path.Combine(Dir, "settings.json");
    public static string LogPath => Path.Combine(Dir, "error.log");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch { /* corrupt settings fall back to defaults */ }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, Json), Json)!;
}
