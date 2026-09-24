using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SoundMatrix;

public sealed class DeviceRow : Observable
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Detail { get; init; }

    bool _show;
    public bool Show { get => _show; set { if (Set(ref _show, value)) Raise(nameof(NumberText)); } }

    int _number;
    public int Number { get => _number; set { if (Set(ref _number, value)) Raise(nameof(NumberText)); } }
    public string NumberText => Show && Number is >= 1 and <= 9 ? Number.ToString() : "–";

    string _color = AppSettings.Palette[0];
    public string Color
    {
        get => _color;
        set { if (Set(ref _color, value)) Raise(nameof(ColorBrush)); }
    }
    public Brush ColorBrush => new SolidColorBrush(AppSettings.ParseColor(Color));
}

public sealed class HotkeyRow : Observable
{
    public required HotkeyAction Action { get; init; }
    public required string Label { get; init; }

    Hotkey _hotkey;
    public Hotkey Hotkey { get => _hotkey; set { if (Set(ref _hotkey, value)) Raise(nameof(Display)); } }

    bool _isCapturing;
    public bool IsCapturing { get => _isCapturing; set { if (Set(ref _isCapturing, value)) Raise(nameof(Display)); } }

    public string Display => IsCapturing ? "Press keys…" : Hotkey.Display;

    string? _problem;
    public string? Problem { get => _problem; set { if (Set(ref _problem, value)) Raise(nameof(HasProblem)); } }
    public bool HasProblem => Problem is not null;
}

/// <summary>Settings, shown inside the overlay in place of the matrices.</summary>
public partial class SettingsPanel : UserControl
{
    static readonly (HotkeyAction Action, string Label)[] HotkeyLabels =
    [
        (HotkeyAction.ToggleOverlay, "Open / close the overlay"),
        (HotkeyAction.OpenSettings, "Open / close settings"),
        (HotkeyAction.NavigateUp, "Move up"),
        (HotkeyAction.NavigateDown, "Move down"),
        (HotkeyAction.NavigateLeft, "Move left"),
        (HotkeyAction.NavigateRight, "Move right"),
        (HotkeyAction.PickUp, "Pick up an app to move it"),
        (HotkeyAction.ToggleMute, "Mute / unmute"),
        (HotkeyAction.VolumeUp, "Volume up"),
        (HotkeyAction.VolumeDown, "Volume down"),
        (HotkeyAction.Close, "Close / cancel / back"),
    ];

    readonly App _app;
    readonly AppSettings _settings;
    readonly ObservableCollection<DeviceRow> _devices = [];
    readonly ObservableCollection<HotkeyRow> _hotkeys = [];
    HotkeyRow? _capturing;

    /// <summary>Raised with the new settings on save, or null on cancel.</summary>
    public event Action<AppSettings?>? Finished;

    public SettingsPanel(App app, AppSettings working)
    {
        _app = app;
        _settings = working;
        InitializeComponent();

        foreach (var d in _settings.Order(_app.Audio.ListDevices(), d => d.Id))
        {
            _settings.EnsureColor(d.Id);
            var row = new DeviceRow
            {
                Id = d.Id,
                Name = d.Name,
                Detail = d.IsDefault ? "Windows default output" : "Output device",
                Show = !_settings.IsHidden(d.Id),
                Color = _settings.DeviceColors[d.Id],
            };
            row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DeviceRow.Show)) Renumber(); };
            _devices.Add(row);
        }
        Renumber();
        DeviceRows.ItemsSource = _devices;

        foreach (var (action, label) in HotkeyLabels)
            _hotkeys.Add(new HotkeyRow { Action = action, Label = label, Hotkey = _settings.Get(action) });
        HotkeyRows.ItemsSource = _hotkeys;

        LoadGeneral(_settings.VolumeStep, _settings.BackdropDim, _settings.BlurBackdrop);
        Validate();

        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) => MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    /// <summary>Called when the overlay removes the panel, so a pending capture can't leave hotkeys suspended.</summary>
    public void Detach() => StopCapture();

    void LoadGeneral(int step, double dim, bool blur)
    {
        StepSlider.Value = step;
        DimSlider.Value = dim;
        BlurCheck.IsChecked = blur;
    }

    void Renumber()
    {
        int n = 1;
        foreach (var row in _devices) row.Number = row.Show ? n++ : 0;
    }

    // ---- keyboard ----------------------------------------------------------

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = e.KeyboardDevice.Modifiers; // same as Keyboard.Modifiers for real input; lets tests simulate Ctrl etc.

        if (_capturing is not null)
        {
            e.Handled = true;
            if (Hotkey.IsModifierKey(key)) return; // wait for the actual key
            _capturing.Hotkey = new Hotkey(key, mods);
            StopCapture();
            Validate();
            return;
        }

        // Keys are matched against the *saved* bindings, since those are what the user knows right now.
        var saved = _app.Settings;
        if (saved.Get(HotkeyAction.Close).Matches(key, mods) || saved.Get(HotkeyAction.OpenSettings).Matches(key, mods))
        {
            e.Handled = true;
            Finished?.Invoke(null);
        }
        else if (key == Key.S && mods == ModifierKeys.Control)
        {
            e.Handled = true;
            if (SaveButton.IsEnabled) Save();
        }
    }

    void Capture_Click(object sender, RoutedEventArgs e) => ToggleCapture((HotkeyRow)((FrameworkElement)sender).Tag);

    /// <summary>Test hook: same as clicking that hotkey's key button.</summary>
    internal void BeginCapture(HotkeyAction action) => ToggleCapture(_hotkeys.First(r => r.Action == action));

    internal HotkeyRow HotkeyRowFor(HotkeyAction action) => _hotkeys.First(r => r.Action == action);

    void ToggleCapture(HotkeyRow row)
    {
        var wasCapturing = _capturing == row;
        StopCapture();
        if (wasCapturing) return;
        _capturing = row;
        row.IsCapturing = true;
        _app.SuspendHotkeys();
    }

    void StopCapture()
    {
        if (_capturing is null) return;
        _capturing.IsCapturing = false;
        _capturing = null;
        _app.ResumeHotkeys();
    }

    void ResetHotkey_Click(object sender, RoutedEventArgs e)
    {
        var row = (HotkeyRow)((FrameworkElement)sender).Tag;
        row.Hotkey = Hotkey.Parse(AppSettings.DefaultHotkeys[row.Action]);
        Validate();
    }

    void Validate()
    {
        foreach (var row in _hotkeys)
        {
            string? problem = null;
            var hk = row.Hotkey;
            if (hk.IsEmpty) problem = null;
            else if (hk.Modifiers == ModifierKeys.None && Hotkey.Digit(hk.Key) is not null)
                problem = "1–9 are reserved for choosing devices";
            else if (_hotkeys.FirstOrDefault(o => o != row && o.Hotkey == hk) is { } other)
                problem = $"Also used by \"{other.Label}\"";
            else if (row.Action == HotkeyAction.ToggleOverlay && hk.Modifiers == ModifierKeys.None && hk.Key is not (>= Key.F1 and <= Key.F24))
                problem = "A system-wide hotkey needs Ctrl, Alt, Shift or Win";
            row.Problem = problem;
        }
        var problems = _hotkeys.Count(r => r.HasProblem);
        ConflictText.Text = problems == 0 ? "" : $"Fix {problems} hotkey {(problems == 1 ? "issue" : "issues")} to save";
        SaveButton.IsEnabled = problems == 0;
    }

    // ---- devices -----------------------------------------------------------

    void MoveUp_Click(object sender, RoutedEventArgs e) => MoveDevice((DeviceRow)((FrameworkElement)sender).Tag, -1);
    void MoveDown_Click(object sender, RoutedEventArgs e) => MoveDevice((DeviceRow)((FrameworkElement)sender).Tag, +1);

    void MoveDevice(DeviceRow row, int delta)
    {
        var i = _devices.IndexOf(row);
        var j = i + delta;
        if (j < 0 || j >= _devices.Count) return;
        _devices.Move(i, j);
        Renumber();
    }

    void Colour_Click(object sender, RoutedEventArgs e)
    {
        var row = (DeviceRow)((FrameworkElement)sender).Tag;
        var i = Array.FindIndex(AppSettings.Palette, p => p.Equals(row.Color, StringComparison.OrdinalIgnoreCase));
        row.Color = AppSettings.Palette[(i + 1) % AppSettings.Palette.Length];
    }

    // ---- general -----------------------------------------------------------

    void StepSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        StepText.Text = $"{StepSlider.Value:0}%";

    void DimSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        DimText.Text = $"{DimSlider.Value * 100:0}%";

    void Reset_Click(object sender, RoutedEventArgs e)
    {
        StopCapture();
        foreach (var row in _hotkeys) row.Hotkey = Hotkey.Parse(AppSettings.DefaultHotkeys[row.Action]);
        LoadGeneral(AppSettings.DefaultVolumeStep, AppSettings.DefaultBackdropDim, true);
        Validate();
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Finished?.Invoke(null);

    void Save_Click(object sender, RoutedEventArgs e) => Save();

    void Save()
    {
        StopCapture();
        foreach (var row in _hotkeys) _settings.Set(row.Action, row.Hotkey);
        _settings.VolumeStep = (int)StepSlider.Value;
        _settings.BackdropDim = Math.Round(DimSlider.Value, 2);
        _settings.BlurBackdrop = BlurCheck.IsChecked == true;

        // Keep the hidden flag for devices that are unplugged right now.
        var present = _devices.Select(d => d.Id).ToHashSet();
        _settings.HiddenDevices = _settings.HiddenDevices.Where(id => !present.Contains(id))
            .Concat(_devices.Where(d => !d.Show).Select(d => d.Id)).ToList();
        _settings.DeviceOrder = _devices.Select(d => d.Id)
            .Concat(_settings.DeviceOrder.Where(id => !present.Contains(id))).ToList();
        foreach (var d in _devices) _settings.DeviceColors[d.Id] = d.Color;

        Finished?.Invoke(_settings);
    }
}
