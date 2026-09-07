using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Sati.LocalBootstrap;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var testRoot = Environment.GetEnvironmentVariable("SATI_LOCAL_INSTALL_ROOT");
        var extractionBase = Environment.GetEnvironmentVariable("SATI_LOCAL_INSTALLER_TEST") == "1" &&
                             !string.IsNullOrWhiteSpace(testRoot)
            ? Path.GetDirectoryName(Path.GetFullPath(testRoot))!
            : Path.Combine(Path.GetTempPath(), "SatiLogica", "Installer");
        var root = Path.Combine(extractionBase, ".sati-bootstrap-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var assembly = Assembly.GetExecutingAssembly();
            Extract(assembly, "SatiPayload.zip", Path.Combine(root, "payload.zip"));
            Extract(assembly, "SqlLocalDB.msi", Path.Combine(root, "SqlLocalDB.msi"));
            ZipFile.ExtractToDirectory(Path.Combine(root, "payload.zip"), root, overwriteFiles: true);

            var script = Path.Combine(root, "Install-SatiLocal.ps1");
            if (!File.Exists(script)) throw new InvalidOperationException("The embedded Sati installer is incomplete.");
            var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = powershell,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = root,
                ArgumentList =
                {
                    "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden",
                    "-ExecutionPolicy", "Bypass", "-File", script
                }
            }) ?? throw new InvalidOperationException("The Sati installation process could not be started.");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "SatiLocalSetup-error.txt"), ex.ToString()); } catch { }
            if (Environment.GetEnvironmentVariable("SATI_LOCAL_INSTALLER_TEST") == "1" &&
                !string.IsNullOrWhiteSpace(testRoot))
            {
                Directory.CreateDirectory(testRoot);
                File.WriteAllText(Path.Combine(testRoot, "bootstrap-error.txt"), ex.ToString());
            }
            MessageBoxW(IntPtr.Zero, "Sati could not be installed.\n\n" + ex.Message,
                "Sati installation failed", 0x00000010);
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void Extract(Assembly assembly, string resourceName, string destination)
    {
        using var source = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' is missing.");
        using var target = File.Create(destination);
        source.CopyTo(target);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
