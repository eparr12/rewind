using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Rewind.Core.Devices;

namespace Rewind.Register.Devices;

/// <summary>
/// Finds USB serial devices in WMI's list of Plug and Play devices (HW-2), which holds only the
/// devices plugged in right now. A port's device ID holds its USB IDs, such as
/// USB\VID_0483&amp;PID_374B&amp;MI_02\…, and its name ends with the COM port, such as
/// "STMicroelectronics STLink Virtual COM Port (COM3)".
/// </summary>
internal sealed partial class WmiDeviceLocator(ILogger<WmiDeviceLocator> logger) : IDeviceLocator
{
    // Everything Device Manager lists under Ports (COM & LPT).
    private const string PortsQuery = "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPClass = 'Ports'";

    // After a failure, later failures go unlogged until WMI works again, so a broken WMI
    // writes two lines to the log instead of one every few seconds.
    private volatile bool _failing;

    public SerialDevice? FindSerialDevice(string vendorId, string productId)
    {
        try
        {
            var device = Search(vendorId, productId);
            WorkingAgain();
            return device;
        }
        catch (Exception e) when (e is ManagementException or COMException)
        {
            if (!_failing)
            {
                _failing = true;
                LogSearchFailed(logger, e);
            }

            return null; // counts as missing, and the reader searches again in a few seconds
        }
    }

    // The reader asks every few seconds, so this looks the one device up by its ID, which takes
    // a few milliseconds, instead of searching the whole list, which takes a quarter of a second.
    public bool IsPluggedIn(string deviceId)
    {
        try
        {
            using var device = new ManagementObject(PathTo(deviceId));
            device.Get();
            WorkingAgain();
            return true;
        }
        catch (ManagementException e) when (e.ErrorCode == ManagementStatus.NotFound)
        {
            WorkingAgain();
            return false;
        }
        catch (Exception e) when (e is ManagementException or COMException)
        {
            if (!_failing)
            {
                _failing = true;
                LogCheckFailed(logger, e);
            }

            return true; // a check that failed says nothing about the scanner, so it keeps its port
        }
    }

    private static SerialDevice? Search(string vendorId, string productId)
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
                    return new SerialDevice(port.Value, id);
                }
            }
        }

        return null;
    }

    // WMI's path to one device, such as Win32_PnPEntity.DeviceID="USB\\VID_2DD6&PID_212A\\…".
    // Inside the quotes, a backslash or a quote needs a backslash before it.
    private static string PathTo(string deviceId)
    {
        var escaped = deviceId
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"Win32_PnPEntity.DeviceID=\"{escaped}\"";
    }

    private void WorkingAgain()
    {
        if (_failing)
        {
            _failing = false;
            LogWorkingAgain(logger);
        }
    }

    // The COM port at the end of a device's name, such as COM3 in "USB Serial Device (COM3)".
    [GeneratedRegex(@"COM[0-9]+(?=\)$)")]
    private static partial Regex PortInName();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't search for USB serial devices; the scanner counts as missing until a search works")]
    private static partial void LogSearchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't check that the scanner is still plugged in; it counts as plugged in until a check works")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "WMI works again")]
    private static partial void LogWorkingAgain(ILogger logger);
}
