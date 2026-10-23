using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Rewind.Core.Devices;

namespace Rewind.Register.Devices;

/// <summary>
/// Finds USB serial devices in WMI's list of Plug and Play devices (HW-2). A port's device ID
/// holds its USB IDs, such as USB\VID_0483&amp;PID_374B&amp;MI_02\…, and its name ends with the
/// COM port, such as "STMicroelectronics STLink Virtual COM Port (COM3)".
/// </summary>
internal sealed partial class WmiDeviceLocator(ILogger<WmiDeviceLocator> logger) : IDeviceLocator
{
    // Everything Device Manager lists under Ports (COM & LPT).
    private const string PortsQuery = "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPClass = 'Ports'";

    // After a failure, later failures go unlogged until a search works again, so a broken WMI
    // writes two lines to the log instead of one every few seconds.
    private volatile bool _failing;

    public string? FindSerialPort(string vendorId, string productId)
    {
        try
        {
            var port = Search(vendorId, productId);
            if (_failing)
            {
                _failing = false;
                LogWorkingAgain(logger);
            }

            return port;
        }
        catch (Exception e) when (e is ManagementException or COMException)
        {
            if (!_failing)
            {
                _failing = true;
                LogFailed(logger, e);
            }

            return null; // counts as missing, and the reader searches again in a few seconds
        }
    }

    private static string? Search(string vendorId, string productId)
    {
        // A composite device, such as the Nucleo's ST-LINK, puts &MI_02 after the IDs, and FTDI
        // adapters write VID_0403+PID_6001, so each ID is looked for on its own.
        var vendor = $"VID_{vendorId}";
        var product = $"PID_{productId}";

        using var searcher = new ManagementObjectSearcher(PortsQuery);
        using var devices = searcher.Get();
        foreach (var device in devices)
        {
            using (device)
            {
                if (device["PNPDeviceID"] is string id
                    && id.Contains(vendor, StringComparison.OrdinalIgnoreCase)
                    && id.Contains(product, StringComparison.OrdinalIgnoreCase)
                    && device["Name"] is string name
                    && PortInName().Match(name) is { Success: true } port)
                {
                    return port.Value;
                }
            }
        }

        return null;
    }

    // The COM port at the end of a device's name, such as COM3 in "USB Serial Device (COM3)".
    [GeneratedRegex(@"COM[0-9]+(?=\)$)")]
    private static partial Regex PortInName();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't search for USB serial devices; the scanner counts as missing until a search works")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Searching for USB serial devices works again")]
    private static partial void LogWorkingAgain(ILogger logger);
}
