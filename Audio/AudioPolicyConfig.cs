using System.Runtime.InteropServices;

namespace SoundMatrix;

/// <summary>
/// Per-app output routing — the same (undocumented) API Windows' "App volume and device preferences"
/// page uses. Windows.Media.Internal.AudioPolicyConfig's activation factory exposes
/// Set/GetPersistedDefaultAudioEndpoint. Called through the raw vtable to avoid WinRT marshalling.
/// </summary>
internal static unsafe class AudioPolicyConfig
{
    const string MmDevApiToken = @"\\?\SWD#MMDEVAPI#";
    const string RenderInterface = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";

    // IUnknown(3) + IInspectable(3) + 19 methods we don't use, then the three we do.
    const int SetPersistedSlot = 25;
    const int GetPersistedSlot = 26;

    const int eRender = 0;
    const int eConsole = 0, eMultimedia = 1;

    static IntPtr _factory;

    static IntPtr Factory
    {
        get
        {
            if (_factory != IntPtr.Zero) return _factory;
            // The interface IID changed in Windows build 21390.
            var iid = Environment.OSVersion.Version.Build >= 21390
                ? new Guid("ab3d4648-e242-459f-b02f-541c70306324")
                : new Guid("2a59116d-6c4f-45e0-a74f-707e3fef9258");
            const string className = "Windows.Media.Internal.AudioPolicyConfig";
            Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out var hClass));
            try { Marshal.ThrowExceptionForHR(RoGetActivationFactory(hClass, ref iid, out _factory)); }
            finally { WindowsDeleteString(hClass); }
            return _factory;
        }
    }

    /// <summary>Routes a process to a device. Pass null to clear, so the app follows the system default.</summary>
    public static void SetDefaultEndpoint(uint pid, string? deviceId)
    {
        var factory = Factory;
        IntPtr hDevice = IntPtr.Zero;
        if (!string.IsNullOrEmpty(deviceId))
        {
            var full = MmDevApiToken + deviceId + RenderInterface;
            Marshal.ThrowExceptionForHR(WindowsCreateString(full, full.Length, out hDevice));
        }
        try
        {
            var vtbl = *(IntPtr**)factory;
            var set = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int, IntPtr, int>)vtbl[SetPersistedSlot];
            Marshal.ThrowExceptionForHR(set(factory, pid, eRender, eConsole, hDevice));
            Marshal.ThrowExceptionForHR(set(factory, pid, eRender, eMultimedia, hDevice));
        }
        finally
        {
            if (hDevice != IntPtr.Zero) WindowsDeleteString(hDevice);
        }
    }

    /// <summary>The MMDevice id a process is pinned to, or null if it follows the default.</summary>
    public static string? GetDefaultEndpoint(uint pid)
    {
        try
        {
            var factory = Factory;
            var vtbl = *(IntPtr**)factory;
            var get = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int, IntPtr*, int>)vtbl[GetPersistedSlot];
            IntPtr hDevice;
            if (get(factory, pid, eRender, eMultimedia, &hDevice) < 0 || hDevice == IntPtr.Zero) return null;
            try
            {
                var raw = WindowsGetStringRawBuffer(hDevice, out var len);
                var s = new string((char*)raw, 0, (int)len);
                if (s.StartsWith(MmDevApiToken, StringComparison.OrdinalIgnoreCase)) s = s[MmDevApiToken.Length..];
                if (s.EndsWith(RenderInterface, StringComparison.OrdinalIgnoreCase)) s = s[..^RenderInterface.Length];
                return s.Length == 0 ? null : s;
            }
            finally { WindowsDeleteString(hDevice); }
        }
        catch
        {
            return null;
        }
    }

    [DllImport("combase.dll")]
    static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);
}
