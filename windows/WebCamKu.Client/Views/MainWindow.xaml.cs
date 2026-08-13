using System.Windows;
using WebCamKu.Core.Networking;

namespace WebCamKu.Client.Views;

public partial class MainWindow : Window
{
    private CancellationTokenSource? cancellation;
    private WebCamConnection? connection;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortTextBox.Text, out var port) || port is < 1 or > 65535)
        {
            StatusText.Text = "Port must be between 1 and 65535.";
            return;
        }
        ConnectButton.IsEnabled = false;
        DisconnectButton.IsEnabled = true;
        cancellation = new CancellationTokenSource();
        connection = new WebCamConnection();
        connection.StatusChanged += status => Dispatcher.Invoke(() => StatusText.Text = status);
        connection.StatisticsChanged += stats => Dispatcher.Invoke(() =>
            StatisticsText.Text = $"Frames: {stats.FramesReceived:N0} | Config: {stats.ConfigPackets:N0} | Bytes: {stats.BytesReceived:N0}");
        try
        {
            await connection.RunAsync(HostTextBox.Text.Trim(), port, cancellation.Token);
        }
        catch (OperationCanceledException) { StatusText.Text = "Disconnected"; }
        catch (Exception error) { StatusText.Text = $"Connection error: {error.Message}"; }
        finally
        {
            if (connection is not null) await connection.DisposeAsync();
            connection = null;
            cancellation?.Dispose();
            cancellation = null;
            ConnectButton.IsEnabled = true;
            DisconnectButton.IsEnabled = false;
        }
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e) => cancellation?.Cancel();

    protected override void OnClosed(EventArgs e)
    {
        cancellation?.Cancel();
        base.OnClosed(e);
    }
}
