using System.Windows.Input;
using Xunit;

namespace SoundMatrix.Tests;

/// <summary>
/// End-to-end: drive the real overlay with its default hotkeys and check both what the user sees
/// (focus, panels, settings) and what the audio system was told to do (the fake's state).
///
/// Fake layout with default settings (apps sorted by name, three tiles per row):
///   1 Speakers:   A Very Long… | Google Chrome (100%) | Microsoft Edge
///                 Notification Sounds | Spotify | Visual Studio Code
///   2 Headphones: Discord | Microsoft Teams | Steam (muted)
///                 Zoom Workplace
///   3 LG monitor: Counter-Strike 2 | OBS Studio (muted) | VLC media player
///   4 DELL:       Media Player
///   5 CABLE:      (empty)
/// </summary>
[Collection(AppCollection.Name)]
public sealed class OverlayHotkeyTests
{
    const string LongName = "A Very Long Application Name That Should Truncate";

    readonly AppDriver _app;

    public OverlayHotkeyTests(AppDriver app)
    {
        _app = app;
        _app.Reset();
    }

    [Fact]
    public void Opens_with_every_device_numbered_and_the_first_app_selected()
    {
        Assert.True(_app.App.IsTestMode);
        Assert.Same(_app.Audio, _app.App.Audio); // never the real audio system
        Assert.True(_app.OverlayVisible);
        Assert.Equal([1, 2, 3, 4, 5], _app.OnUi(() => _app.Overlay.Devices.Select(d => d.Number).ToArray()));
        Assert.Equal(LongName, _app.Focused);
    }

    [Fact]
    public void Arrow_keys_move_between_tiles_and_across_devices()
    {
        _app.Press(Key.Right);
        Assert.Equal("Google Chrome", _app.Focused);

        _app.Press(Key.Right);
        Assert.Equal("Microsoft Edge", _app.Focused);

        _app.Press(Key.Down);
        Assert.Equal("Visual Studio Code", _app.Focused);

        _app.Press(Key.Up);
        Assert.Equal("Microsoft Edge", _app.Focused);

        _app.Press(Key.Right); // off the edge of device 1 into device 2
        Assert.Equal("Discord", _app.Focused);

        _app.Press(Key.Left);
        Assert.Equal("Microsoft Edge", _app.Focused);
    }

    [Fact]
    public void Number_keys_jump_to_that_device()
    {
        _app.Press(Key.D3);
        Assert.Equal("Counter-Strike 2", _app.Focused);

        _app.Press(Key.NumPad2);
        Assert.Equal("Discord", _app.Focused);

        _app.Press(Key.D5); // empty device: focus stays put
        Assert.Equal("Discord", _app.Focused);
    }

    [Fact]
    public void Enter_then_a_number_moves_the_app_to_that_device()
    {
        _app.Press(Key.D2);
        _app.Press(Key.Enter);

        Assert.Equal("Discord", _app.Held);
        Assert.True(_app.Tile("Discord").IsHeld);
        var targets = _app.OnUi(() => _app.Overlay.Devices.Where(d => d.IsTarget).Select(d => d.Number).ToArray());
        Assert.Equal([1, 3, 4, 5], targets);

        _app.Press(Key.D4);

        Assert.Equal(FakeAudioService.DeviceIdForNumber(4), _app.Audio.Inspect("Discord").DeviceId);
        Assert.Equal(4, _app.DeviceNumberOf("Discord"));
        Assert.Null(_app.Held);
        Assert.Equal("Discord", _app.Focused);
        Assert.Contains("Moved Discord", _app.Status);
    }

    [Fact]
    public void Moving_to_the_device_it_is_already_on_changes_nothing()
    {
        _app.Press(Key.Enter);
        _app.Press(Key.D1);

        Assert.Equal(FakeAudioService.DeviceIdForNumber(1), _app.Audio.Inspect(LongName).DeviceId);
        Assert.Null(_app.Held);
        Assert.Contains("already", _app.Status);
    }

    [Fact]
    public void Escape_cancels_a_pick_up_then_closes_the_overlay()
    {
        _app.Press(Key.Enter);
        Assert.Equal(LongName, _app.Held);

        _app.Press(Key.Escape);
        Assert.Null(_app.Held);
        Assert.True(_app.OverlayVisible);
        Assert.Equal(1, _app.DeviceNumberOf(LongName));

        _app.Press(Key.Escape);
        Assert.False(_app.OverlayVisible);
    }

    [Fact]
    public void Enter_again_cancels_a_pick_up()
    {
        _app.Press(Key.Enter);
        _app.Press(Key.Enter);

        Assert.Null(_app.Held);
        Assert.All(_app.OnUi(() => _app.Overlay.Devices.ToList()), d => Assert.False(d.IsTarget));
    }

    [Fact]
    public void Space_toggles_mute()
    {
        _app.Press(Key.Space);
        Assert.True(_app.Audio.Inspect(LongName).Muted);
        Assert.True(_app.Tile(LongName).IsMuted);

        _app.Press(Key.Space);
        Assert.False(_app.Audio.Inspect(LongName).Muted);
        Assert.False(_app.Tile(LongName).IsMuted);
    }

    [Fact]
    public void Volume_keys_change_the_level_by_the_step()
    {
        // Starts at 50%, default step 5%.
        _app.Press(Key.OemPlus);
        Assert.Equal(0.55f, _app.Audio.Inspect(LongName).Volume, 3);

        _app.Press(Key.OemMinus, Key.OemMinus);
        Assert.Equal(0.45f, _app.Audio.Inspect(LongName).Volume, 3);
        Assert.Equal("45%", _app.Tile(LongName).VolumeText);
    }

    [Fact]
    public void Volume_cannot_go_above_100_percent()
    {
        _app.Press(Key.Right); // Google Chrome, already at 100%
        _app.Press(Key.OemPlus);

        Assert.Equal(1.0f, _app.Audio.Inspect("Google Chrome").Volume, 3);
    }

    [Fact]
    public void Ctrl_comma_opens_settings_in_place_of_the_matrix_and_Escape_returns()
    {
        _app.Press(Key.OemComma, ModifierKeys.Control);
        Assert.True(_app.SettingsOpen);
        Assert.Equal(System.Windows.Visibility.Collapsed, _app.OnUi(() => _app.Overlay.MatrixView.Visibility));

        _app.Press(Key.Escape);
        Assert.False(_app.SettingsOpen);
        Assert.True(_app.OverlayVisible);

        _app.Press(Key.OemComma, ModifierKeys.Control);
        _app.Press(Key.OemComma, ModifierKeys.Control); // the same key closes it again
        Assert.False(_app.SettingsOpen);
    }

    [Fact]
    public void Matrix_keys_do_nothing_while_settings_are_open()
    {
        _app.Press(Key.OemComma, ModifierKeys.Control);
        _app.Press(Key.Space);

        Assert.False(_app.Audio.Inspect(LongName).Muted);
    }

    [Fact]
    public void A_hotkey_rebound_in_settings_works_once_saved()
    {
        _app.Press(Key.OemComma, ModifierKeys.Control);
        _app.OnUi(() => _app.Overlay.CurrentSettingsPanel!.BeginCapture(HotkeyAction.VolumeUp));
        _app.Press(Key.V);
        Assert.Equal(new Hotkey(Key.V, ModifierKeys.None),
            _app.OnUi(() => _app.Overlay.CurrentSettingsPanel!.HotkeyRowFor(HotkeyAction.VolumeUp).Hotkey));

        _app.Press(Key.S, ModifierKeys.Control); // save
        Assert.False(_app.SettingsOpen);
        Assert.Equal(new Hotkey(Key.V, ModifierKeys.None), _app.App.Settings.Get(HotkeyAction.VolumeUp));

        _app.Press(Key.V);
        Assert.Equal(0.55f, _app.Audio.Inspect(LongName).Volume, 3);

        _app.Press(Key.OemPlus); // the old binding no longer does anything
        Assert.Equal(0.55f, _app.Audio.Inspect(LongName).Volume, 3);
    }

    [Fact]
    public void A_duplicate_hotkey_cannot_be_saved()
    {
        _app.Press(Key.OemComma, ModifierKeys.Control);
        _app.OnUi(() => _app.Overlay.CurrentSettingsPanel!.BeginCapture(HotkeyAction.VolumeUp));
        _app.Press(Key.Space); // already Mute

        Assert.True(_app.OnUi(() => _app.Overlay.CurrentSettingsPanel!.HotkeyRowFor(HotkeyAction.VolumeUp).HasProblem));

        _app.Press(Key.S, ModifierKeys.Control);
        Assert.True(_app.SettingsOpen);
        Assert.Equal(new Hotkey(Key.OemPlus, ModifierKeys.None), _app.App.Settings.Get(HotkeyAction.VolumeUp));
    }

    [Fact]
    public void The_global_hotkey_is_registered_and_toggles_the_overlay()
    {
        Assert.Single(_app.OnUi(() => _app.App.Hotkeys.RegisteredIds.ToList()));

        _app.SendGlobalHotkey();
        Assert.False(_app.OverlayVisible);

        _app.SendGlobalHotkey();
        Assert.True(_app.OverlayVisible);
    }
}
