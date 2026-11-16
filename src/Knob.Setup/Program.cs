using System;
using System.Linq;
using System.Windows.Forms;

namespace Knob.Setup;

internal static class Program
{
    /// <summary>
    /// KnobSetup.exe            — the installer window
    /// KnobSetup.exe /S         — silent install with the defaults (/D=folder to pick the folder, /NOLAUNCH)
    /// KnobSetup.exe /FILESONLY /D=folder — just unpack the app (no shortcuts, registry or closing Knob); for testing
    /// Exit code 0 = installed, 1 = failed.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        bool Has(string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        var dir = args.FirstOrDefault(a => a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase))?.Substring(3);

        if (Has("/S") || Has("/FILESONLY"))
        {
            var o = new InstallOptions { FilesOnly = Has("/FILESONLY"), LaunchWhenDone = !Has("/NOLAUNCH") && !Has("/FILESONLY") };
            if (dir is not null) o.Dir = dir;
            else if (Install.InstalledDir() is string existing) o.Dir = existing;
            try
            {
                Installer.Run(o, (_, s) => Console.WriteLine(s));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
        return 0;
    }
}
