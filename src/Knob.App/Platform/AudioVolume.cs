using System.Runtime.InteropServices;

namespace Knob.App.Platform;

/// <summary>
/// Master volume of the default playback device (Core Audio). Every call looks the default device up again,
/// so switching to headphones just works. Returns null / false when there is no audio device.
/// </summary>
internal static class AudioVolume
{
    public static float? Get() => With(v => { Check(v.GetMasterVolumeLevelScalar(out var l)); return (float?)l; });

    public static bool? IsMuted() => With(v => { Check(v.GetMute(out var m)); return (bool?)m; });

    public static bool Set(float level) => With(v =>
    {
        var ctx = Guid.Empty;
        Check(v.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref ctx));
        if (level > 0) Check(v.SetMute(false, ref ctx)); // turning it up unmutes, like the volume keys
        return true;
    });

    public static bool SetMuted(bool muted) => With(v =>
    {
        var ctx = Guid.Empty;
        Check(v.SetMute(muted, ref ctx));
        return true;
    });

    private static T? With<T>(Func<IAudioEndpointVolume, T> action)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? volume = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            Check(enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device));
            var iid = typeof(IAudioEndpointVolume).GUID;
            Check(device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out volume));
            return action((IAudioEndpointVolume)volume);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return default;
        }
        finally
        {
            if (volume is not null) Marshal.ReleaseComObject(volume);
            if (device is not null) Marshal.ReleaseComObject(device);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
    }

    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
