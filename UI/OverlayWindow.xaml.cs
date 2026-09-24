using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SoundMatrix;

public partial class OverlayWindow : Window
{
    readonly App _app;
    readonly Dictionary<string, DeviceVM> _deviceVMs = [];
    readonly Dictionary<string, AppVM> _appVMs = [];
    readonly Dictionary<AppVM, FrameworkElement> _tiles = [];
    readonly DispatcherTimer _refreshTimer;
    readonly DispatcherTimer _meterTimer;
    Dictionary<string, AudioApp> _audioApps = [];
    AppVM? _focused;
    AppVM? _held;

    public ObservableCollection<DeviceVM> Devices { get; } = [];
    public ObservableCollection<HintVM> Hints { get; } = [];

    AppSettings Settings => _app.Settings;

    public OverlayWindow(App app)
    {
        _app = app;
        InitializeComponent();
        DataContext = this;

        _refreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => RefreshAudio(), Dispatcher);
        _meterTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Render, (_, _) => UpdateMeters(), Dispatcher);
        _refreshTimer.Stop();
        _meterTimer.Stop();

        PreviewKeyDown += OnPreviewKeyDown;
        Activated += (_, _) => Trace.Log("Overlay activated");
        Deactivated += (_, _) => { Trace.Log("Overlay deactivated"); HideOverlay(); };
        SizeChanged += (_, _) => DeviceList.MaxWidth = Math.Max(560, ActualWidth - 96);
    }

    // ---- show / hide -------------------------------------------------------

    public void ShowOverlay()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        ApplyAppearance();

        RefreshAudio();
        CoverCursorMonitor(hwnd);
        Show();
        CoverCursorMonitor(hwnd); // again, in case a DPI change on show resized us
        Activate();
        Native.SetForegroundWindow(hwnd);
        Keyboard.Focus(this);
        Trace.Log($"ShowOverlay done: active={IsActive} devices={Devices.Count} apps={_appVMs.Count}");

        SetStatus(null);
        _refreshTimer.Start();
        _meterTimer.Start();
    }

    public void HideOverlay()
    {
        if (!IsVisible) return;
        _refreshTimer.Stop();
        _meterTimer.Stop();
        CancelHold();
        CloseSettings();
        Hide();
    }

    void ApplyAppearance()
    {
        Dim.Opacity = Math.Clamp(Settings.BackdropDim, 0, 0.95);
        Native.SetBlurBehind(new WindowInteropHelper(this).EnsureHandle(), Settings.BlurBackdrop);
    }

    // ---- settings (shown in place of the matrix) ---------------------------

    SettingsPanel? _settingsPanel;
    bool SettingsOpen => _settingsPanel is not null;

    public void OpenSettings()
    {
        if (SettingsOpen) return;
        CancelHold();
        _settingsPanel = new SettingsPanel(_app, Settings.Clone());
        _settingsPanel.Finished += saved =>
        {
            if (saved is not null) _app.ApplySettings(saved);
            CloseSettings();
            if (saved is null) return;
            ApplyAppearance();
            RefreshAudio();
            SetStatus("Settings saved");
        };
        SettingsHost.Content = _settingsPanel;
        SettingsHost.Visibility = Visibility.Visible;
        MatrixView.Visibility = Visibility.Collapsed;
        _meterTimer.Stop();
        StatusText.Text = "Settings";
        UpdateHints();
    }

    void CloseSettings()
    {
        if (_settingsPanel is null) return;
        _settingsPanel.Detach();
        _settingsPanel = null;
        SettingsHost.Content = null;
        SettingsHost.Visibility = Visibility.Collapsed;
        MatrixView.Visibility = Visibility.Visible;
        if (IsVisible) _meterTimer.Start();
        Keyboard.Focus(this);
        SetStatus(null);
        UpdateHints();
    }

    static void CoverCursorMonitor(IntPtr hwnd)
    {
        Native.GetCursorPos(out var pt);
        var monitor = Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return;
        var r = info.rcMonitor;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Native.SWP_NOACTIVATE);
    }

    // ---- audio -> view models ---------------------------------------------

    void RefreshAudio()
    {
        var snap = _app.Audio.Refresh();
        _audioApps = snap.Apps.ToDictionary(a => a.Key);

        bool coloursChanged = false;
        foreach (var d in snap.Devices) coloursChanged |= Settings.EnsureColor(d.Id);
        if (coloursChanged) Settings.Save();

        // Devices, numbered in the user's order, skipping hidden ones.
        var visible = Settings.Order(snap.Devices, d => d.Id).Where(d => !Settings.IsHidden(d.Id)).ToList();
        var deviceVMs = new List<DeviceVM>();
        for (int i = 0; i < visible.Count; i++)
        {
            var d = visible[i];
            if (!_deviceVMs.TryGetValue(d.Id, out var vm)) _deviceVMs[d.Id] = vm = new DeviceVM(d.Id);
            vm.Name = d.Name;
            vm.IsDefault = d.IsDefault;
            vm.Number = i + 1;
            vm.Color = Settings.ColorFor(d.Id);
            deviceVMs.Add(vm);
        }
        if (!Devices.SequenceEqual(deviceVMs))
        {
            Devices.Clear();
            foreach (var vm in deviceVMs) Devices.Add(vm);
        }

        // Apps, grouped under the device they're playing on.
        var seen = new HashSet<string>();
        foreach (var deviceVM in deviceVMs)
        {
            var desired = new List<AppVM>();
            foreach (var app in snap.Apps.Where(a => a.DeviceId == deviceVM.Id))
            {
                if (!_appVMs.TryGetValue(app.Key, out var vm)) _appVMs[app.Key] = vm = new AppVM(app.Key);
                vm.Name = app.Name;
                vm.Icon = app.Icon;
                vm.Volume = app.Volume;
                vm.IsMuted = app.Muted;
                vm.Device = deviceVM;
                desired.Add(vm);
                seen.Add(app.Key);
            }
            if (!deviceVM.Apps.SequenceEqual(desired))
            {
                deviceVM.Apps.Clear();
                foreach (var vm in desired) deviceVM.Apps.Add(vm);
            }
        }
        foreach (var key in _appVMs.Keys.Where(k => !seen.Contains(k)).ToList()) _appVMs.Remove(key);

        if (_held is not null && !_appVMs.ContainsKey(_held.Key)) CancelHold();
        if (_focused is null || !_appVMs.ContainsKey(_focused.Key))
            SetFocus(Devices.SelectMany(d => d.Apps).FirstOrDefault());

        EmptyText.Visibility = Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = snap.Devices.Count == 0
            ? "No active output devices found."
            : $"All output devices are hidden.\nPress {Settings.Get(HotkeyAction.OpenSettings).Display} to choose which ones get a matrix.";

        UpdateHints();
    }

    void UpdateMeters()
    {
        foreach (var (key, vm) in _appVMs)
            if (_audioApps.TryGetValue(key, out var app))
                vm.Peak = Math.Max(app.Peak, vm.Peak * 0.8); // quick attack, smooth decay
    }

    // ---- keyboard ----------------------------------------------------------

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (SettingsOpen) return; // the settings panel handles its own keys (Tab, Space, capture, Esc)

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        bool Is(HotkeyAction action) => Settings.Get(action).Matches(key, mods);

        e.Handled = true;
        if (Is(HotkeyAction.Close)) { if (_held is not null) CancelHold(); else HideOverlay(); }
        else if (Is(HotkeyAction.OpenSettings)) OpenSettings();
        else if (Is(HotkeyAction.NavigateLeft)) Navigate(-1, 0);
        else if (Is(HotkeyAction.NavigateRight)) Navigate(1, 0);
        else if (Is(HotkeyAction.NavigateUp)) Navigate(0, -1);
        else if (Is(HotkeyAction.NavigateDown)) Navigate(0, 1);
        else if (Is(HotkeyAction.PickUp)) TogglePickUp();
        else if (Is(HotkeyAction.ToggleMute)) ToggleMute();
        else if (Is(HotkeyAction.VolumeUp)) AdjustVolume(+1);
        else if (Is(HotkeyAction.VolumeDown)) AdjustVolume(-1);
        else if (mods == ModifierKeys.None && Hotkey.Digit(key) is int n) DeviceNumber(n);
        else e.Handled = false;
    }

    /// <summary>Spatial navigation: nearest tile in the pressed direction, across device panels.</summary>
    void Navigate(int dx, int dy)
    {
        if (_focused is null || !_tiles.TryGetValue(_focused, out var fromTile))
        {
            SetFocus(Devices.SelectMany(d => d.Apps).FirstOrDefault());
            return;
        }
        var from = Center(fromTile);
        AppVM? best = null;
        double bestScore = double.MaxValue;
        foreach (var (vm, tile) in _tiles)
        {
            if (vm == _focused || !tile.IsVisible) continue;
            var v = Center(tile) - from;
            double along = dx != 0 ? v.X * dx : v.Y * dy;
            double across = dx != 0 ? Math.Abs(v.Y) : Math.Abs(v.X);
            if (along < 1) continue;
            double score = along + across * 2.5;
            if (score < bestScore) { bestScore = score; best = vm; }
        }
        if (best is not null) SetFocus(best);
    }

    Point Center(FrameworkElement e) => e.TranslatePoint(new Point(e.ActualWidth / 2, e.ActualHeight / 2), Root);

    void SetFocus(AppVM? vm)
    {
        if (_focused == vm) return;
        if (_focused is not null) _focused.IsFocused = false;
        _focused = vm;
        if (vm is not null) vm.IsFocused = true;
    }

    void TogglePickUp()
    {
        if (_held is not null) { CancelHold(); SetStatus(null); return; }
        if (_focused is null) return;
        _held = _focused;
        _held.IsHeld = true;
        foreach (var d in Devices) d.IsTarget = d != _held.Device;
        SetStatus($"Moving {_held.Name} — press a device number");
        UpdateHints();
    }

    void CancelHold()
    {
        if (_held is not null) _held.IsHeld = false;
        _held = null;
        foreach (var d in Devices) d.IsTarget = false;
        UpdateHints();
    }

    void DeviceNumber(int n)
    {
        var device = Devices.FirstOrDefault(d => d.Number == n);
        if (device is null) { SetStatus($"There's no device {n}"); return; }

        if (_held is null)
        {
            // Not moving anything: jump to that device's first app.
            if (device.Apps.FirstOrDefault() is { } first) SetFocus(first);
            return;
        }

        var held = _held;
        CancelHold();
        if (held.Device == device) { SetStatus($"{held.Name} is already on {device.Name}"); return; }
        if (!_audioApps.TryGetValue(held.Key, out var app)) return;
        try
        {
            _app.Audio.Move(app, device.Id);
            SetStatus($"Moved {held.Name} to {device.Name}");
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't move {held.Name}: {ex.Message}");
        }
        RefreshAudio();
        SetFocus(held);
    }

    void ToggleMute()
    {
        if (_focused is null || !_audioApps.TryGetValue(_focused.Key, out var app)) return;
        var mute = !_focused.IsMuted;
        _app.Audio.SetMute(app, mute);
        _focused.IsMuted = mute;
        SetStatus($"{_focused.Name} {(mute ? "muted" : "unmuted")}");
    }

    void AdjustVolume(int direction)
    {
        if (_focused is null || !_audioApps.TryGetValue(_focused.Key, out var app)) return;
        int step = Math.Clamp(Settings.VolumeStep, 1, 50);
        double pct = _focused.Volume * 100;
        // Snap to the step grid so 73% → 75% / 70%, not 78% / 68%.
        double next = direction > 0
            ? Math.Floor(pct / step + 1e-6) * step + step
            : Math.Ceiling(pct / step - 1e-6) * step - step;
        next = Math.Clamp(next, 0, 100);
        _app.Audio.SetVolume(app, (float)(next / 100));
        _focused.Volume = next / 100;
        SetStatus($"{_focused.Name}  {next:0}%");
    }

    void SetStatus(string? text) =>
        StatusText.Text = text ?? $"Select an app and press {Settings.Get(HotkeyAction.PickUp).Display} to move it to another device";

    void UpdateHints()
    {
        string K(HotkeyAction a) => Settings.Get(a).Display;
        var numbers = Devices.Count switch { 0 => "1", 1 => "1", var c => $"1–{Math.Min(c, 9)}" };

        Hints.Clear();
        if (SettingsOpen)
        {
            Hints.Add(new("Tab", "Next option"));
            Hints.Add(new("Space", "Toggle / choose"));
            Hints.Add(new("←→", "Adjust slider"));
            Hints.Add(new("Ctrl+S", "Save"));
            Hints.Add(new(K(HotkeyAction.Close), "Back to matrix"));
        }
        else if (_held is not null)
        {
            Hints.Add(new(numbers, $"Send {_held.Name} to device"));
            Hints.Add(new($"{K(HotkeyAction.PickUp)} / {K(HotkeyAction.Close)}", "Cancel"));
        }
        else
        {
            var arrows = string.Concat(new[] { HotkeyAction.NavigateLeft, HotkeyAction.NavigateUp, HotkeyAction.NavigateDown, HotkeyAction.NavigateRight }
                .Select(a => Settings.Get(a).Display));
            Hints.Add(new(arrows, "Navigate"));
            Hints.Add(new(K(HotkeyAction.PickUp), "Pick up"));
            Hints.Add(new(numbers, "Jump to device"));
            Hints.Add(new(K(HotkeyAction.ToggleMute), "Mute"));
            Hints.Add(new($"{K(HotkeyAction.VolumeUp)} {K(HotkeyAction.VolumeDown)}", $"Volume ±{Settings.VolumeStep}%"));
            Hints.Add(new(K(HotkeyAction.OpenSettings), "Settings"));
            Hints.Add(new(K(HotkeyAction.Close), "Close"));
        }
    }

    // ---- mouse (nice to have) ---------------------------------------------

    void Tile_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AppVM vm } fe) _tiles[vm] = fe;
    }

    void Tile_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AppVM vm } fe && _tiles.TryGetValue(vm, out var t) && t == fe)
            _tiles.Remove(vm);
    }

    void Tile_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AppVM vm }) return;
        SetFocus(vm);
        if (e.ClickCount == 2) TogglePickUp();
        e.Handled = true;
    }

    void Tile_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AppVM vm }) return;
        SetFocus(vm);
        AdjustVolume(Math.Sign(e.Delta));
        e.Handled = true;
    }

    void Backdrop_MouseDown(object sender, MouseButtonEventArgs e) => HideOverlay();
}
