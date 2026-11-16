using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Knob.App.Platform;

/// <summary>Explorer's icon for a file, folder, drive or shortcut (cached by path), as a frozen WPF image.</summary>
internal static class ShellIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string path)
    {
        if (Cache.TryGetValue(path, out var cached)) return cached;
        ImageSource? image = null;
        var info = new SHFILEINFO();
        var attrs = Directory.Exists(path) ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        var flags = SHGFI_ICON | SHGFI_LARGEICON;
        if (!File.Exists(path) && !Directory.Exists(path)) flags |= SHGFI_USEFILEATTRIBUTES; // generic icon for a missing item
        if (SHGetFileInfo(path, attrs, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags) != IntPtr.Zero && info.hIcon != IntPtr.Zero)
        {
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                image = src;
            }
            catch (Exception ex) when (ex is COMException or ArgumentException)
            {
            }
            finally
            {
                NativeMethods.DestroyIcon(info.hIcon);
            }
        }
        Cache[path] = image;
        return image;
    }

    /// <summary>Short display name: file name without .lnk/.url/.exe, folder name, or "C:" for a drive.</summary>
    public static string DisplayName(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length <= 2) return trimmed; // "C:"
        var name = Path.GetFileName(trimmed);
        var ext = Path.GetExtension(name);
        return ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".url", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)
            : name;
    }

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>Open with the default handler (run an app, open a folder or document).</summary>
    public static bool Open(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Open Explorer with the item selected.</summary>
    public static void ShowInFolder(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref SHFILEINFO info, uint size, uint flags);
}
