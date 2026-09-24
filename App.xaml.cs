using System.IO;
using System.Windows;

namespace SoundMatrix;

public partial class App : Application
{
    Mutex? _mutex;
    EventWaitHandle? _showEvent;
    GlobalHotkeys _hotkeys = null!;
    TrayIcon _tray = null!;
    OverlayWindow _overlay = null!;

    public AppSettings Settings { get; private set; } = new();
    public IAudioService Audio { get; private set; } = null!;

    /// <summary>Development test mode: stubbed audio, separate settings, can run beside the real app.</summary>
    public bool IsTestMode { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

#if DEBUG
        IsTestMode = e.Args.Contains("--fake-audio");
#endif
        var instance = IsTestMode ? "SoundMatrix.Test" : "SoundMatrix";
        var showEventName = instance + ".ShowOverlay";

        _mutex = new Mutex(true, instance + ".SingleInstance", out var isFirst);
        if (!isFirst)
        {
            // Already running (e.g. launched again from the Start menu): ask that copy to open the overlay.
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            try { using var show = EventWaitHandle.OpenExisting(showEventName); show.Set(); } catch { }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            ex.Handled = true;
            try { File.AppendAllText(AppSettings.LogPath, $"[{DateTime.Now:u}] {ex.Exception}\n\n"); } catch { }
            _tray?.Notify("SoundMatrix hit an error", ex.Exception.Message);
        };

        if (IsTestMode) AppSettings.Profile = "settings.test"; // keep fake devices out of the real settings
        Settings = AppSettings.Load();
#if DEBUG
        Audio = IsTestMode ? new FakeAudioService() : new WasapiAudioService();
#else
        Audio = new WasapiAudioService();
#endif

        if (e.Args.Contains("--diagnose"))
        {
            var path = Path.Combine(Path.GetDirectoryName(AppSettings.LogPath)!, "diagnose.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Audio.Describe());
            Shutdown();
            return;
        }
        _hotkeys = new GlobalHotkeys();
        _tray = new TrayIcon(ToggleOverlay, OpenSettings, ExitApp, IsTestMode);
        _overlay = new OverlayWindow(this);

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, showEventName);
        ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => { if (!_overlay.IsVisible) _overlay.ShowOverlay(); }),
            null, Timeout.Infinite, executeOnlyOnce: false);

        var hotkey = Settings.Get(HotkeyAction.ToggleOverlay);
        var registered = RegisterHotkeys();
        if (IsTestMode)
            _tray.Notify("SoundMatrix test mode", "Fake devices and apps. Nothing here touches your real audio.");
        else if (registered)
            _tray.Notify("SoundMatrix is running", $"Press {hotkey.Display} to open the matrix.");

        if (e.Args.Contains("--show") || IsTestMode) _overlay.ShowOverlay();
        if (e.Args.Contains("--settings")) OpenSettings();
    }

    bool RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var hotkey = Settings.Get(HotkeyAction.ToggleOverlay);
        _tray.SetOpenHotkey(hotkey);
        if (hotkey.IsEmpty) return false;
        if (_hotkeys.Register(hotkey, ToggleOverlay)) return true;
        // In test mode the installed copy usually owns the hotkey; the tray icon still opens the overlay.
        if (!IsTestMode) _tray.Notify("Hotkey unavailable", $"{hotkey.Display} is already used by another app. Pick another in Settings.");
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
