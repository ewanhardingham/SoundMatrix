using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SoundMatrix;

/// <summary>
/// Development-only stand-in for the real audio system (Debug builds, <c>--fake-audio</c>).
/// Stubs devices and apps covering the awkward cases: a crowded device, an empty one, long names,
/// muted and silent apps, and an app that keeps appearing and disappearing. Volume, mute and
/// moves only change this in-memory model, so nothing touches your real audio setup.
/// </summary>
public sealed class FakeAudioService : IAudioService
{
    sealed class FakeApp(string name, int device, float volume, float activity, string color, bool muted = false, bool blinks = false)
    {
        public string Key { get; } = "fake:" + name;
        public string Name { get; } = name;
        public string DeviceId { get; set; } = DeviceIdFor(device);
        public float Volume { get; set; } = volume;
        public bool Muted { get; set; } = muted;
        /// <summary>How loud it plays, 0 = silent.</summary>
        public float Activity { get; } = activity;
        /// <summary>Present for 8 seconds, gone for 8, to exercise apps appearing / vanishing.</summary>
        public bool Blinks { get; } = blinks;
        public Color Color { get; } = (Color)ColorConverter.ConvertFromString(color);
        public double Phase { get; } = name.Length * 0.7;
        public ImageSource Icon => _icon ??= MakeIcon(Name, Color);
        ImageSource? _icon;
    }

    static string DeviceIdFor(int index) => $"fake-device-{index}";

    readonly List<AudioDevice> _devices =
    [
        new(DeviceIdFor(0), "Speakers (Realtek High Definition Audio)", IsDefault: true),
        new(DeviceIdFor(1), "Headphones (Arctis Nova 7 Wireless)", IsDefault: false),
        new(DeviceIdFor(2), "LG ULTRAGEAR 27GP850 (NVIDIA High Definition Audio)", IsDefault: false),
        new(DeviceIdFor(3), "Monitor Speakers (DELL U2723QE)", IsDefault: false),
        new(DeviceIdFor(4), "CABLE Input (VB-Audio Virtual Cable)", IsDefault: false),
    ];

    readonly List<FakeApp> _apps =
    [
        new("Spotify", 0, 0.80f, 0.90f, "#1DB954"),
        new("Google Chrome", 0, 1.00f, 0.60f, "#4285F4"),
        new("Microsoft Edge", 0, 0.65f, 0.00f, "#0C8CE9"),
        new("Visual Studio Code", 0, 0.30f, 0.00f, "#23A9F2"),
        new("A Very Long Application Name That Should Truncate", 0, 0.50f, 0.20f, "#9CA3AF"),
        new("Notification Sounds", 0, 0.50f, 0.40f, "#F59E0B", blinks: true),
        new("Discord", 1, 1.00f, 0.50f, "#5865F2"),
        new("Microsoft Teams", 1, 0.90f, 0.30f, "#6264A7"),
        new("Steam", 1, 0.40f, 0.00f, "#1B2838", muted: true),
        new("Zoom Workplace", 1, 0.70f, 0.00f, "#2D8CFF"),
        new("Counter-Strike 2", 2, 0.75f, 1.00f, "#DE9B35"),
        new("OBS Studio", 2, 1.00f, 0.40f, "#302E31", muted: true),
        new("VLC media player", 2, 0.55f, 0.80f, "#FF8800"),
        new("Media Player", 3, 0.35f, 0.70f, "#E81123"),
    ];

    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Random _noise = new(42);
    readonly bool _blink;
    AudioSnapshot? _current;

    /// <param name="blink">Let "Notification Sounds" come and go. Tests turn this off to stay deterministic.</param>
    public FakeAudioService(bool blink = true) => _blink = blink;

    /// <summary>Test hook: the fake's own state for an app, i.e. what "Windows" would now report.</summary>
    internal (string DeviceId, float Volume, bool Muted) Inspect(string appName)
    {
        var app = _apps.Single(a => a.Name == appName);
        return (app.DeviceId, app.Volume, app.Muted);
    }

    /// <summary>Device id for the device shown as number <paramref name="number"/> (1-based) with default settings.</summary>
    internal static string DeviceIdForNumber(int number) => DeviceIdFor(number - 1);

    public AudioSnapshot Current => _current ?? Refresh();

    public AudioSnapshot Refresh()
    {
        var snap = new AudioSnapshot();
        snap.Devices.AddRange(_devices);
        foreach (var fake in _apps.Where(IsPresent).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            snap.Apps.Add(new AudioApp
            {
                Key = fake.Key,
                Name = fake.Name,
                Icon = fake.Icon,
                DeviceId = fake.DeviceId,
                Volume = fake.Volume,
                Muted = fake.Muted,
                ReadPeak = () => Peak(fake),
            });
        }
        return _current = snap;
    }

    public void SetVolume(AudioApp app, float volume) { if (Find(app) is { } f) f.Volume = Math.Clamp(volume, 0f, 1f); }
    public void SetMute(AudioApp app, bool mute) { if (Find(app) is { } f) f.Muted = mute; }
    public void Move(AudioApp app, string deviceId) { if (Find(app) is { } f) f.DeviceId = deviceId; }

    public string Describe()
    {
        var sb = new StringBuilder("FAKE AUDIO (development test mode)\n");
        foreach (var d in _devices)
        {
            sb.AppendLine($"DEVICE {d.Name}{(d.IsDefault ? "  [default]" : "")}");
            foreach (var a in _apps.Where(a => a.DeviceId == d.Id))
                sb.AppendLine($"    {a.Name}  {a.Volume:P0}{(a.Muted ? "  MUTED" : "")}{(IsPresent(a) ? "" : "  (hidden right now)")}");
        }
        return sb.ToString();
    }

    FakeApp? Find(AudioApp app) => _apps.FirstOrDefault(a => a.Key == app.Key);

    bool IsPresent(FakeApp app) => !_blink || !app.Blinks || (int)(_clock.Elapsed.TotalSeconds / 8) % 2 == 0;

    /// <summary>A wobbling level so the meters look alive: silent and muted apps stay at zero.</summary>
    float Peak(FakeApp app)
    {
        if (app.Muted || app.Activity <= 0) return 0;
        var t = _clock.Elapsed.TotalSeconds;
        var wave = 0.55 + 0.3 * Math.Sin(t * 2.3 + app.Phase) + 0.15 * Math.Sin(t * 7.1 + app.Phase * 2);
        var jitter = 0.85 + 0.15 * _noise.NextDouble();
        return (float)Math.Clamp(app.Activity * app.Volume * wave * jitter, 0, 1);
    }

    /// <summary>A coloured rounded tile with the app's initial, standing in for a real exe icon.</summary>
    static ImageSource MakeIcon(string name, Color color)
    {
        const int size = 64;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(color), null, new Rect(0, 0, size, size), 14, 14);
            var text = new FormattedText(name[..1].ToUpperInvariant(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                34, Brushes.White, pixelsPerDip: 1.0);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
