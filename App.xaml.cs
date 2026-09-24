using System.IO;
using System.Windows;

namespace SoundMatrix;

public partial class App : Application
{
    const string ShowEventName = "SoundMatrix.ShowOverlay";

    Mutex? _mutex;
    EventWaitHandle? _showEvent;
    GlobalHotkeys _hotkeys = null!;
    TrayIcon _tray = null!;
    OverlayWindow _overlay = null!;

    public AppSettings Settings { get; private set; } = new();
    public AudioService Audio { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "SoundMatrix.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            // Already running (e.g. launched again from the Start menu): ask that copy to open the overlay.
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            try { using var show = EventWaitHandle.OpenExisting(ShowEventName); show.Set(); } catch { }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            ex.Handled = true;
            try { File.AppendAllText(AppSettings.LogPath, $"[{DateTime.Now:u}] {ex.Exception}\n\n"); } catch { }
            _tray?.Notify("SoundMatrix hit an error", ex.Exception.Message);
        };

        Settings = AppSettings.Load();
        Audio = new AudioService();

        if (e.Args.Contains("--diagnose"))
        {
            var path = Path.Combine(Path.GetDirectoryName(AppSettings.LogPath)!, "diagnose.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Audio.Describe());
            Shutdown();
            return;
        }
        _hotkeys = new GlobalHotkeys();
        _tray = new TrayIcon(ToggleOverlay, OpenSettings, ExitApp);
        _overlay = new OverlayWindow(this);

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => { if (!_overlay.IsVisible) _overlay.ShowOverlay(); }),
            null, Timeout.Infinite, executeOnlyOnce: false);

        var hotkey = Settings.Get(HotkeyAction.ToggleOverlay);
        if (RegisterHotkeys())
            _tray.Notify("SoundMatrix is running", $"Press {hotkey.Display} to open the matrix.");

        if (e.Args.Contains("--show")) _overlay.ShowOverlay();
        if (e.Args.Contains("--settings")) OpenSettings();
    }

    bool RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var hotkey = Settings.Get(HotkeyAction.ToggleOverlay);
        _tray.SetOpenHotkey(hotkey);
        if (hotkey.IsEmpty) return false;
        if (_hotkeys.Register(hotkey, ToggleOverlay)) return true;
        _tray.Notify("Hotkey unavailable", $"{hotkey.Display} is already used by another app. Pick another in Settings.");
        return false;
    }

    /// <summary>Released while settings record a new key, so the old binding can't fire.</summary>
    public void SuspendHotkeys() => _hotkeys.UnregisterAll();
    public void ResumeHotkeys() => RegisterHotkeys();

    public void ApplySettings(AppSettings saved)
    {
        Settings = saved;
        Settings.Save();
        RegisterHotkeys();
    }

    void ToggleOverlay()
    {
        Trace.Log($"ToggleOverlay visible={_overlay.IsVisible}");
        if (_overlay.IsVisible) _overlay.HideOverlay();
        else _overlay.ShowOverlay();
    }

    /// <summary>Settings live inside the overlay, so open it first if needed.</summary>
    void OpenSettings()
    {
        if (!_overlay.IsVisible) _overlay.ShowOverlay();
        _overlay.OpenSettings();
    }

    void ExitApp()
    {
        _tray.Dispose();
        _hotkeys.Dispose();
        Shutdown();
    }
}
