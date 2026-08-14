using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using System.Net.Sockets;
using System.IO;
using WebCamKu.Client.Services;
using WebCamKu.Core.Networking;

namespace WebCamKu.Client.Views;

public partial class MainWindow : Window
{
    private CancellationTokenSource? cancellation;
    private WebCamConnection? connection;
    private VideoDecodePipeline? decodePipeline;
    private WriteableBitmap? bitmap;
    private long renderedFrames;
    private DateTime statisticsStarted;
    private DateTime previousStatisticsAt;
    private long previousRenderedFrames;
    private long previousReceivedBytes;
    private ConnectionStatistics latestStatistics = new(0, 0, 0, 0);
    private readonly DispatcherTimer statisticsTimer;
    private readonly object frameGate = new();
    private readonly SharedFrameProvider sharedFrames = new();
    private byte[]? renderPixels;
    private int renderWidth;
    private int renderHeight;
    private bool renderScheduled;
    private readonly AdbUsbController usbController = new();

    public MainWindow()
    {
        InitializeComponent();
        statisticsTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, UpdateStatistics, Dispatcher);
        Loaded += (_, _) =>
        {
            if (Environment.GetEnvironmentVariable("WEBCAMKU_TEST_USB") == "1")
            {
                ConnectionModeComboBox.SelectedIndex = 1;
                ConnectButton_Click(ConnectButton, new RoutedEventArgs());
                return;
            }
            var testHost = Environment.GetEnvironmentVariable("WEBCAMKU_TEST_HOST");
            if (!string.IsNullOrWhiteSpace(testHost))
            {
                HostTextBox.Text = testHost;
                ConnectButton_Click(ConnectButton, new RoutedEventArgs());
            }
        };
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortTextBox.Text, out var port) || port is < 1 or > 65535)
        {
            StatusText.Text = "Port must be between 1 and 65535.";
            return;
        }
        var useUsb = ConnectionModeComboBox.SelectedIndex == 1;
        var connectionHost = useUsb ? "127.0.0.1" : HostTextBox.Text.Trim();
        ConnectButton.IsEnabled = false;
        ConnectionModeComboBox.IsEnabled = false;
        DisconnectButton.IsEnabled = true;
        cancellation = new CancellationTokenSource();
        statisticsStarted = DateTime.UtcNow;
        previousStatisticsAt = statisticsStarted;
        previousRenderedFrames = 0;
        previousReceivedBytes = 0;
        renderedFrames = 0;
        var reconnectAttempt = 0;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                if (useUsb)
                {
                    try { StatusText.Text = await usbController.EnsureForwardAsync(port, cancellation.Token); }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
                    catch (Exception error)
                    {
                        var delay = ReconnectPolicy.DelayForAttempt(reconnectAttempt++);
                        StatusText.Text = $"USB: {error.Message} Retrying in {delay.TotalSeconds:0}sâ€¦";
                        await Task.Delay(delay, cancellation.Token);
                        continue;
                    }
                }
                var activeConnection = new WebCamConnection();
                var activePipeline = new VideoDecodePipeline();
                connection = activeConnection;
                decodePipeline = activePipeline;
                activePipeline.FrameDecoded += OnFrameDecoded;
                activeConnection.EncodedPacketReceived += activePipeline.Submit;
                activeConnection.StatusChanged += status => Dispatcher.Invoke(() =>
                {
                    StatusText.Text = status;
                    if (status == "Streaming")
                    {
                        reconnectAttempt = 0;
                        SetControlsEnabled(true);
                    }
                });
                activeConnection.StatisticsChanged += stats => latestStatistics = stats;
                try
                {
                    await activeConnection.RunAsync(connectionHost, port, cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    SetControlsEnabled(false);
                    var delay = ReconnectPolicy.DelayForAttempt(reconnectAttempt++);
                    StatusText.Text = $"Connection lost: {FriendlyMessage(error)} Reconnecting in {delay.TotalSeconds:0}s…";
                    await Task.Delay(delay, cancellation.Token);
                }
                finally
                {
                    await activeConnection.DisposeAsync();
                    await activePipeline.DisposeAsync();
                    if (ReferenceEquals(connection, activeConnection)) connection = null;
                    if (ReferenceEquals(decodePipeline, activePipeline)) decodePipeline = null;
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (useUsb) await usbController.CleanupAsync();
            cancellation?.Dispose();
            cancellation = null;
            StatusText.Text = "Disconnected";
            ConnectButton.IsEnabled = true;
            ConnectionModeComboBox.IsEnabled = true;
            DisconnectButton.IsEnabled = false;
            SetControlsEnabled(false);
        }
    }

    private void ConnectionModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (HostTextBox is null) return;
        var usb = ConnectionModeComboBox.SelectedIndex == 1;
        HostTextBox.IsEnabled = !usb;
        if (usb) StatusText.Text = "USB mode uses ADB forwarding to 127.0.0.1.";
        else if (StatusText.Text.StartsWith("USB mode", StringComparison.Ordinal)) StatusText.Text = "Disconnected";
    }

    private static string FriendlyMessage(Exception error) => error switch
    {
        TimeoutException => error.Message,
        SocketException => "The phone is unreachable or Wi-Fi is unavailable.",
        EndOfStreamException => error.Message,
        _ => error.Message,
    };

    private void DisconnectButton_Click(object sender, RoutedEventArgs e) => cancellation?.Cancel();

    private async void StartVirtualCameraButton_Click(object sender, RoutedEventArgs e) =>
        await RunVirtualCameraCommandAsync("start", "WebCamKu Camera started");

    private async void StopVirtualCameraButton_Click(object sender, RoutedEventArgs e) =>
        await RunVirtualCameraCommandAsync("stop", "WebCamKu Camera stopped");

    private async Task RunVirtualCameraCommandAsync(string command, string success)
    {
        try
        {
            await VirtualCameraController.RunAsync(command);
            StatusText.Text = success;
        }
        catch (Exception error) { StatusText.Text = $"Virtual camera: {error.Message}"; }
    }

    private async void SwitchCameraButton_Click(object sender, RoutedEventArgs e)
    {
        if (await SendControlAsync("switchCamera", null, SwitchCameraButton))
        {
            TorchButton.IsChecked = false;
            ZoomSlider.Value = 1;
            ZoomText.Text = "1.0x";
        }
    }

    private async void TorchButton_Click(object sender, RoutedEventArgs e)
    {
        var requested = TorchButton.IsChecked == true;
        if (!await SendControlAsync("torch", requested, TorchButton)) TorchButton.IsChecked = !requested;
    }

    private async void ZoomSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ZoomText.Text = $"{ZoomSlider.Value:F1}x";
        await SendControlAsync("zoom", ZoomSlider.Value, ZoomSlider);
    }

    private async Task<bool> SendControlAsync(string name, object? value, UIElement control)
    {
        if (connection is null) return false;
        control.IsEnabled = false;
        try
        {
            var acknowledgement = await connection.SendCommandAsync(name, value, cancellation?.Token ?? default);
            StatusText.Text = acknowledgement.Success ? $"{name} applied" : $"{name}: {acknowledgement.Error}";
            return acknowledgement.Success;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            StatusText.Text = $"{name}: {error.Message}";
            return false;
        }
        finally { control.IsEnabled = connection is not null; }
    }

    private void SetControlsEnabled(bool enabled)
    {
        SwitchCameraButton.IsEnabled = enabled;
        TorchButton.IsEnabled = enabled;
        ZoomSlider.IsEnabled = enabled;
    }

    private void OnFrameDecoded(byte[] pixels, int width, int height, long timestampUs)
    {
        sharedFrames.Publish(pixels, width, height, timestampUs);
        lock (frameGate)
        {
            if (renderScheduled) return;
            renderPixels = pixels;
            renderWidth = width;
            renderHeight = height;
            renderScheduled = true;
        }
        Dispatcher.BeginInvoke(RenderLatestFrame, DispatcherPriority.Render);
    }

    private void RenderLatestFrame()
    {
        byte[] pixels;
        int width;
        int height;
        lock (frameGate)
        {
            if (renderPixels is null) { renderScheduled = false; return; }
            pixels = renderPixels;
            width = renderWidth;
            height = renderHeight;
            renderScheduled = false;
        }
        if (bitmap is null || bitmap.PixelWidth != width || bitmap.PixelHeight != height)
        {
            bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            PreviewImage.Source = bitmap;
        }
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        renderedFrames++;
    }

    private void UpdateStatistics(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var seconds = Math.Max(0.001, (now - previousStatisticsAt).TotalSeconds);
        var fps = (renderedFrames - previousRenderedFrames) / seconds;
        var mbps = (latestStatistics.BytesReceived - previousReceivedBytes) * 8d / seconds / 1_000_000d;
        previousStatisticsAt = now;
        previousRenderedFrames = renderedFrames;
        previousReceivedBytes = latestStatistics.BytesReceived;
        StatisticsText.Text = $"Frames: {renderedFrames:N0} | FPS: {fps:F1} | {mbps:F1} Mbps | " +
            $"Dropped: {decodePipeline?.DroppedFrames ?? 0:N0} | Errors: {decodePipeline?.DecoderErrors ?? 0:N0}";
        Title = $"WebCamKu — {renderedFrames:N0} frames — {fps:F1} FPS";
    }

    protected override void OnClosed(EventArgs e)
    {
        cancellation?.Cancel();
        sharedFrames.Dispose();
        base.OnClosed(e);
    }
}
