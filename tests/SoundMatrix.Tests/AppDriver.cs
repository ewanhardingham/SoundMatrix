using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SoundMatrix.Tests;

/// <summary>
/// Runs the real SoundMatrix app (overlay, settings panel, global hotkey window) on its own UI thread,
/// backed by <see cref="FakeAudioService"/>. Keys are delivered as genuine WPF key events through the same
/// handlers a physical keypress reaches, with modifiers supplied by <see cref="FakeKeyboard"/>.
/// WPF allows one Application per process, so every test shares this instance and calls <see cref="Reset"/>.
/// </summary>
public sealed class AppDriver : IDisposable
{
    App _app = null!;
    FakeKeyboard _keyboard = null!;

    public FakeAudioService Audio { get; private set; } = null!;

    public AppDriver()
    {
        Exception? startupError = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                // WPF raises OnStartup itself once the dispatcher runs; hand it our fake audio and profile.
                Audio = new FakeAudioService(blink: false);
                App.TestStartup = (Audio, "e2e");
                _app = new App();
                _app.InitializeComponent(); // App.xaml resources the overlay depends on
                _app.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        if (_app.Overlay is null) throw new InvalidOperationException("OnStartup didn't create the overlay.");
                        _app.Overlay.HideOnDeactivate = false; // CI desktops shuffle focus; don't let that close the overlay
                        _keyboard = new FakeKeyboard();
                    }
                    catch (Exception ex) { startupError = ex; }
                    ready.Set();
                }, DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex) { startupError = ex; ready.Set(); }
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!ready.Wait(TimeSpan.FromSeconds(60))) throw new TimeoutException("SoundMatrix didn't start within 60s.");
        if (startupError is not null) throw new InvalidOperationException("SoundMatrix failed to start.", startupError);
    }

    /// <summary>Fresh fake audio, default settings, overlay open on the matrix at a fixed size.</summary>
    public void Reset()
    {
        OnUi(() =>
        {
            Audio = new FakeAudioService(blink: false);
            _app.ResetForTests(Audio);
            // A fixed layout (3 device panels per row) regardless of the machine's screen size.
            var overlay = _app.Overlay;
            overlay.Left = 0;
            overlay.Top = 0;
            overlay.Width = 1920;
            overlay.Height = 1080;
        });
        Settle();
    }

    public App App => _app;
    public OverlayWindow Overlay => _app.Overlay;

    // ---- input -------------------------------------------------------------

    public void Press(Key key, ModifierKeys modifiers = ModifierKeys.None)
    {
        OnUi(() =>
        {
            _keyboard.Held = modifiers;
            try
            {
                // Settings take keys when open; otherwise the overlay window does (as with real focus).
                UIElement target = (UIElement?)Overlay.CurrentSettingsPanel ?? Overlay;
                Route(target, key, Keyboard.PreviewKeyDownEvent, Keyboard.KeyDownEvent);
                Route(target, key, Keyboard.PreviewKeyUpEvent, Keyboard.KeyUpEvent);
            }
            finally { _keyboard.Held = ModifierKeys.None; }
        });
        Settle();
    }

    public void Press(params Key[] keys)
    {
        foreach (var key in keys) Press(key);
    }

    void Route(UIElement target, Key key, RoutedEvent preview, RoutedEvent bubble)
    {
        var source = PresentationSource.FromVisual(Overlay)!;
        var tunnel = new KeyEventArgs(_keyboard, source, Environment.TickCount, key) { RoutedEvent = preview };
        target.RaiseEvent(tunnel);
        if (tunnel.Handled) return;
        target.RaiseEvent(new KeyEventArgs(_keyboard, source, Environment.TickCount, key) { RoutedEvent = bubble });
    }

    /// <summary>Delivers a real WM_HOTKEY to the app's hotkey window, as Windows does for Ctrl+Alt+S.</summary>
    public void SendGlobalHotkey()
    {
        OnUi(() =>
        {
            var hotkeys = _app.Hotkeys;
            var id = hotkeys.RegisteredIds.Single();
            SendMessage(hotkeys.Handle, 0x0312 /* WM_HOTKEY */, (IntPtr)id, IntPtr.Zero);
        });
        Settle();
    }

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // ---- state -------------------------------------------------------------

    public string? Focused => OnUi(() => Overlay.FocusedApp?.Name);
    public string? Held => OnUi(() => Overlay.HeldApp?.Name);
    public bool OverlayVisible => OnUi(() => Overlay.IsVisible);
    public bool SettingsOpen => OnUi(() => Overlay.IsSettingsOpen);
    public string Status => OnUi(() => Overlay.StatusMessage);

    /// <summary>The number of the device panel an app's tile is in, or null if it isn't shown.</summary>
    public int? DeviceNumberOf(string app) =>
        OnUi(() => Overlay.Devices.FirstOrDefault(d => d.Apps.Any(a => a.Name == app))?.Number);

    public AppVM Tile(string app) => OnUi(() => Overlay.Devices.SelectMany(d => d.Apps).Single(a => a.Name == app));

    public T OnUi<T>(Func<T> f) => _app.Dispatcher.Invoke(f);
    public void OnUi(Action a) => _app.Dispatcher.Invoke(a);

    /// <summary>Waits for layout, Loaded events and rendering to finish.</summary>
    public void Settle() => _app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    public void Dispose()
    {
        try { _app.Dispatcher.Invoke(_app.ExitApp); } catch { /* already gone */ }
    }
}

/// <summary>A keyboard whose modifier keys are whatever the test says, instead of the physical keyboard's.</summary>
sealed class FakeKeyboard() : KeyboardDevice(InputManager.Current)
{
    public ModifierKeys Held { get; set; }

    protected override KeyStates GetKeyStatesFromSystem(Key key) => key switch
    {
        Key.LeftCtrl when Held.HasFlag(ModifierKeys.Control) => KeyStates.Down,
        Key.LeftAlt when Held.HasFlag(ModifierKeys.Alt) => KeyStates.Down,
        Key.LeftShift when Held.HasFlag(ModifierKeys.Shift) => KeyStates.Down,
        Key.LWin when Held.HasFlag(ModifierKeys.Windows) => KeyStates.Down,
        _ => KeyStates.None,
    };
}

[CollectionDefinition(Name)]
public sealed class AppCollection : ICollectionFixture<AppDriver>
{
    public const string Name = "SoundMatrix app";
}
