using System.Diagnostics;
using System.IO;

namespace WebCamKu.Client.Services;

internal sealed record AdbDevice(string Serial, string State, string Details);

internal sealed class AdbUsbController
{
    private string? adbPath;
    private string? forwardedSerial;
    private int forwardedPort;

    public async Task<string> EnsureForwardAsync(int port, CancellationToken cancellationToken)
    {
        adbPath ??= LocateAdb() ?? throw new InvalidOperationException(
            "ADB was not found. Install Android SDK Platform-Tools or set WEBCAMKU_ADB_PATH.");
        var devicesResult = await RunAsync(adbPath, ["devices", "-l"], cancellationToken);
        if (devicesResult.ExitCode != 0)
            throw new InvalidOperationException($"ADB could not list devices: {UsefulError(devicesResult)}");
        var devices = ParseDevices(devicesResult.Output);
        var unauthorized = devices.FirstOrDefault(device => device.State == "unauthorized");
        if (unauthorized is not null)
            throw new InvalidOperationException("The phone is unauthorized. Unlock it and accept the USB debugging prompt.");
        var online = devices.Where(device => device.State == "device").ToArray();
        if (online.Length == 0)
            throw new InvalidOperationException("No authorized Android device was found over USB.");
        if (online.Length > 1)
            throw new InvalidOperationException("Multiple Android devices are connected. Leave only one device connected.");
        var device = online[0];
        var forward = await RunAsync(adbPath, ["-s", device.Serial, "forward", $"tcp:{port}", $"tcp:{port}"], cancellationToken);
        if (forward.ExitCode != 0)
            throw new InvalidOperationException($"ADB port forwarding failed: {UsefulError(forward)}");
        forwardedSerial = device.Serial;
        forwardedPort = port;
        return $"USB ready: {device.Serial}, localhost:{port}";
    }

    public async Task CleanupAsync()
    {
        if (adbPath is null || forwardedSerial is null || forwardedPort == 0) return;
        try { await RunAsync(adbPath, ["-s", forwardedSerial, "forward", "--remove", $"tcp:{forwardedPort}"], CancellationToken.None); }
        catch { }
        forwardedSerial = null;
        forwardedPort = 0;
    }

    internal static IReadOnlyList<AdbDevice> ParseDevices(string output) => output
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(line => !line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) && !line.StartsWith('*'))
        .Select(line => line.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries))
        .Where(parts => parts.Length >= 2)
        .Select(parts => new AdbDevice(parts[0], parts[1], parts.Length == 3 ? parts[2] : ""))
        .ToArray();

    internal static string? LocateAdb()
    {
        var candidates = new List<string?>
        {
            Environment.GetEnvironmentVariable("WEBCAMKU_ADB_PATH"),
            Path.Combine(Environment.GetEnvironmentVariable("ANDROID_HOME") ?? "", "platform-tools", "adb.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"),
        };
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            candidates.Add(Path.Combine(directory.FullName, ".tools", "android-sdk", "platform-tools", "adb.exe"));
            directory = directory.Parent;
        }
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(value => Path.Combine(value.Trim('"'), "adb.exe")));
        return candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate));
    }

    private static string UsefulError(ProcessResult result) =>
        string.IsNullOrWhiteSpace(result.Error) ? result.Output.Trim() : result.Error.Trim();

    private static async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw; }
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
