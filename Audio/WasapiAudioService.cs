using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace SoundMatrix;

public sealed record AppSession(string DeviceId, AudioSessionControl Control, bool IsActive);

/// <summary>The real audio system: WASAPI sessions plus per-app routing via AudioPolicyConfig.</summary>
public sealed class WasapiAudioService : IAudioService
{
    readonly MMDeviceEnumerator _enumerator = new();
    readonly Dictionary<string, (string? DeviceId, DateTime Until)> _recentMoves = [];
    AudioSnapshot? _current;

    public AudioSnapshot Current => _current ?? Refresh();

    public AudioSnapshot Refresh()
    {
        var next = Capture();
        _current?.Dispose();
        _current = next;
        return next;
    }

    public string Describe()
    {
        var snap = Refresh();
        var names = snap.Devices.ToDictionary(d => d.Id, d => d.Name);
        string Name(string? id) => id is null ? "(follows default)" : names.GetValueOrDefault(id, id);
        var sb = new System.Text.StringBuilder();
        foreach (var d in snap.Devices) sb.AppendLine($"DEVICE {d.Name}{(d.IsDefault ? "  [default]" : "")}");
        foreach (var app in snap.Apps)
        {
            sb.AppendLine($"APP {app.Name} -> shown on: {Name(app.DeviceId)}");
            foreach (var pid in app.Pids) sb.AppendLine($"    pid {pid} pinned: {Name(AudioPolicyConfig.GetDefaultEndpoint(pid))}");
            foreach (var s in app.Sessions) sb.AppendLine($"    session on {Name(s.DeviceId)}{(s.IsActive ? "  ACTIVE" : "")}");
        }
        return sb.ToString();
    }

    public void SetVolume(AudioApp app, float volume)
    {
        foreach (var s in app.Sessions)
            try { s.Control.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f); } catch { }
    }

    public void SetMute(AudioApp app, bool mute)
    {
        foreach (var s in app.Sessions)
            try { s.Control.SimpleAudioVolume.Mute = mute; } catch { }
    }

    public void Move(AudioApp app, string deviceId)
    {
        // Moving to the default device clears the override so the app keeps following the default.
        var isDefault = Current.Devices.FirstOrDefault(d => d.Id == deviceId)?.IsDefault == true;
        foreach (var pid in app.Pids)
            AudioPolicyConfig.SetDefaultEndpoint(pid, isDefault ? null : deviceId);
        // Remember the move: briefly overrides stale "active" sessions, and keeps an idle app
        // on its new device until it next plays (see Place).
        _recentMoves[app.Key] = (deviceId, DateTime.UtcNow.AddSeconds(3));
    }

    AudioSnapshot Capture()
    {
        var snap = new AudioSnapshot();
        string? defaultId = null;
        try
        {
            var def = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            defaultId = def.ID;
            snap.Owned.Add(def);
        }
        catch { /* no output devices */ }

        var apps = new Dictionary<string, AudioApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            snap.Owned.Add(device);
            var deviceId = device.ID;
            snap.Devices.Add(new AudioDevice(deviceId, SafeName(device), deviceId == defaultId));

            SessionCollection sessions;
            try
            {
                var manager = device.AudioSessionManager;
                manager.RefreshSessions();
                sessions = manager.Sessions;
            }
            catch { continue; }

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                snap.Owned.Add(session);
                try
                {
                    if (session.IsSystemSoundsSession || session.State == AudioSessionState.AudioSessionStateExpired) continue;
                    var pid = session.GetProcessID;
                    if (pid == 0) continue;
                    var info = ProcessInfo.Get(pid);
                    if (info is null) continue;

                    if (!apps.TryGetValue(info.Path, out var app))
                        apps[info.Path] = app = new AudioApp { Key = info.Path, Name = info.Name, Icon = info.Icon };
                    if (!app.Pids.Contains(pid)) app.Pids.Add(pid);
                    app.Sessions.Add(new AppSession(deviceId, session, session.State == AudioSessionState.AudioSessionStateActive));
                }
                catch { /* session vanished mid-read */ }
            }
        }

        var deviceIds = snap.Devices.Select(d => d.Id).ToHashSet();
        foreach (var app in apps.Values)
        {
            app.DeviceId = Place(app, deviceIds, defaultId);
            ReadLevels(app);
        }

        snap.Apps.AddRange(apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase));
        return snap;
    }

    /// <summary>
    /// Works out which device an app is really playing on. Where audio is actually flowing wins;
    /// the Windows per-app pin is only trusted when the app has a session there, because apps that
    /// pick their own output (Discord, games) ignore it.
    /// </summary>
    string Place(AudioApp app, HashSet<string> deviceIds, string? defaultId)
    {
        _recentMoves.TryGetValue(app.Key, out var move);
        var justMoved = move.DeviceId is not null && move.Until > DateTime.UtcNow && deviceIds.Contains(move.DeviceId);

        // 1. Just moved: old sessions can report "active" for a moment while Windows reroutes.
        if (justMoved) return move.DeviceId!;

        var pinned = app.Pids.Select(AudioPolicyConfig.GetDefaultEndpoint).FirstOrDefault(id => id is not null && deviceIds.Contains(id));

        // 2. Currently playing: that's the truth (prefer the pinned device if it's playing on several).
        var active = app.Sessions.Where(s => s.IsActive).ToList();
        if (active.Count > 0)
        {
            _recentMoves.Remove(app.Key);
            return active.FirstOrDefault(s => s.DeviceId == pinned)?.DeviceId ?? active[0].DeviceId;
        }

        // 3. Moved by us while idle: it has no session on the new device until it next plays.
        if (move.DeviceId is not null && deviceIds.Contains(move.DeviceId)) return move.DeviceId;

        // 4. Pinned in Windows and has a session there.
        if (pinned is not null && app.Sessions.Any(s => s.DeviceId == pinned)) return pinned;

        // 5. Otherwise the default device if it has a session there, else wherever its session is.
        if (defaultId is not null && app.Sessions.Any(s => s.DeviceId == defaultId)) return defaultId;
        return app.Sessions[0].DeviceId;
    }

    /// <summary>Volume and mute come from the session on the app's device; the meter is the loudest session.</summary>
    static void ReadLevels(AudioApp app)
    {
        var primary = (app.Sessions.FirstOrDefault(s => s.DeviceId == app.DeviceId) ?? app.Sessions[0]).Control;
        app.Volume = Safe(() => primary.SimpleAudioVolume.Volume);
        app.Muted = Safe(() => primary.SimpleAudioVolume.Mute);
        var sessions = app.Sessions.Select(s => s.Control).ToArray();
        app.ReadPeak = () => sessions.Max(s => Safe(() => s.AudioMeterInformation.MasterPeakValue));
    }

    static T Safe<T>(Func<T> f) { try { return f(); } catch { return default!; } }

    static string SafeName(MMDevice device)
    {
        try { return device.FriendlyName; } catch { return "Unknown device"; }
    }
}

/// <summary>Friendly name + icon for a process, cached by executable path.</summary>
internal sealed record ProcessInfo(string Path, string Name, ImageSource? Icon)
{
    static readonly Dictionary<uint, string?> PathByPid = [];
    static readonly Dictionary<string, ProcessInfo> ByPath = new(StringComparer.OrdinalIgnoreCase);

    public static ProcessInfo? Get(uint pid)
    {
        if (!PathByPid.TryGetValue(pid, out var path))
            PathByPid[pid] = path = Native.GetProcessPath(pid);
        if (path is null) return null;
        if (!ByPath.TryGetValue(path, out var info))
            ByPath[path] = info = new ProcessInfo(path, FriendlyName(path), LoadIcon(path));
        return info;
    }

    static string FriendlyName(string path)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            if (!string.IsNullOrEmpty(description)) return description;
        }
        catch { }
        return System.IO.Path.GetFileNameWithoutExtension(path);
    }

    static ImageSource? LoadIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch { return null; }
    }
}
