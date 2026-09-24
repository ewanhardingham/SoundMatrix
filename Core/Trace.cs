using System.IO;

namespace SoundMatrix;

/// <summary>Opt-in diagnostics: set SOUNDMATRIX_TRACE=1 to log to %APPDATA%\SoundMatrix\trace.log.</summary>
internal static class Trace
{
    static readonly bool Enabled = Environment.GetEnvironmentVariable("SOUNDMATRIX_TRACE") == "1";

    public static void Log(string message)
    {
        if (!Enabled) return;
        try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(AppSettings.LogPath)!, "trace.log"), $"{DateTime.Now:HH:mm:ss.fff} {message}\n"); }
        catch { }
    }
}
