using System.Diagnostics;
using System.IO;

namespace WebCamKu.Client.Services;

internal static class VirtualCameraController
{
    public static async Task<string> RunAsync(string command, CancellationToken cancellationToken = default)
    {
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "WebCamKu", "VirtualCamera", "WebCamKu.VirtualCamera.Manager.exe");
        var manager = File.Exists(installed)
            ? installed
            : Path.Combine(AppContext.BaseDirectory, "WebCamKu.VirtualCamera.Manager.exe");
        if (!File.Exists(manager)) throw new FileNotFoundException(
            "Virtual camera manager is not installed. Run scripts/windows/install-virtual-camera.ps1 as Administrator.", manager);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(manager, command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException((await error).Trim());
        return (await output).Trim();
    }
}
