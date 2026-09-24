using System.Windows.Media;

namespace SoundMatrix;

/// <summary>
/// Everything the UI needs from the audio system. <see cref="WasapiAudioService"/> is the real one;
/// <see cref="FakeAudioService"/> stubs devices and apps for development (Debug builds, --fake-audio).
/// </summary>
public interface IAudioService
{
    AudioSnapshot Current { get; }
    AudioSnapshot Refresh();
    void SetVolume(AudioApp app, float volume);
    void SetMute(AudioApp app, bool mute);
    void Move(AudioApp app, string deviceId);
    /// <summary>Human-readable dump of where every app is and why, for --diagnose.</summary>
    string Describe();

    List<AudioDevice> ListDevices() => Current.Devices.ToList();
}

public sealed record AudioDevice(string Id, string Name, bool IsDefault);

/// <summary>One application (grouped by executable) and the device it's playing on.</summary>
public sealed class AudioApp
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public ImageSource? Icon { get; init; }
    public string DeviceId { get; set; } = "";
    public float Volume { get; set; }
    public bool Muted { get; set; }

    /// <summary>Live output level 0–1, polled ~25 times a second while the overlay is open.</summary>
    public Func<float> ReadPeak { get; set; } = () => 0;
    public float Peak => ReadPeak();

    // WASAPI backend bookkeeping.
    internal List<uint> Pids { get; } = [];
    internal List<AppSession> Sessions { get; } = [];
}

public sealed class AudioSnapshot : IDisposable
{
    public List<AudioDevice> Devices { get; } = [];
    public List<AudioApp> Apps { get; } = [];
    internal List<IDisposable> Owned { get; } = [];

    public void Dispose()
    {
        foreach (var d in Owned) { try { d.Dispose(); } catch { } }
        Owned.Clear();
    }
}
