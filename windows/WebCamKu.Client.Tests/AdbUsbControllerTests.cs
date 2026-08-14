using WebCamKu.Client.Services;
using Xunit;

namespace WebCamKu.Client.Tests;

public sealed class AdbUsbControllerTests
{
    [Fact]
    public void ParsesAuthorizedUnauthorizedAndOfflineDevices()
    {
        const string output = "List of devices attached\r\n" +
            "SERIAL1 device product:m31 model:SM_M315F transport_id:5\r\n" +
            "SERIAL2 unauthorized usb:1-2 transport_id:6\r\n" +
            "SERIAL3 offline transport_id:7\r\n";

        var devices = AdbUsbController.ParseDevices(output);

        Assert.Collection(devices,
            device => { Assert.Equal("SERIAL1", device.Serial); Assert.Equal("device", device.State); Assert.Contains("SM_M315F", device.Details); },
            device => Assert.Equal("unauthorized", device.State),
            device => Assert.Equal("offline", device.State));
    }

    [Fact]
    public void IgnoresDaemonAndHeaderLines()
    {
        var devices = AdbUsbController.ParseDevices("* daemon started successfully *\nList of devices attached\n\n");
        Assert.Empty(devices);
    }
}
