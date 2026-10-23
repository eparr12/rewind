namespace Rewind.Core.Devices;

/// <summary>
/// How Rewind finds and talks to the barcode scanner (NET-11). Device Manager shows its IDs
/// in the scanner's Properties, on the Details tab under Hardware Ids, as
/// USB\VID_xxxx&amp;PID_yyyy.
/// </summary>
public sealed class ScannerOptions
{
    /// <summary>The settings section these options are read from.</summary>
    public const string SectionName = "Scanner";

    /// <summary>The speed every Rewind device uses: 115200 8N1 (HW-1).</summary>
    public const int DefaultBaudRate = 115200;

    private const int UsbIdLength = 4;

    /// <summary>The scanner's USB vendor ID: four hex digits, such as 0483.</summary>
    public string VendorId { get; set; } = "";

    /// <summary>The scanner's USB product ID: four hex digits, such as 374B.</summary>
    public string ProductId { get; set; } = "";

    /// <summary>
    /// The serial speed. A scanner with its own USB serial port ignores it, but a USB-to-serial
    /// bridge, such as the Nucleo's ST-LINK, passes it on to the chip at the other end.
    /// </summary>
    public int BaudRate { get; set; } = DefaultBaudRate;

    /// <summary>Whether an ID is four hex digits, the form Device Manager shows.</summary>
    public static bool IsUsbId(string? id) => id is { Length: UsbIdLength } && id.All(char.IsAsciiHexDigit);
}
