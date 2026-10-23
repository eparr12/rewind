namespace Rewind.Core.Devices;

/// <summary>
/// Finds a USB serial device's COM port from its USB IDs, never from a saved COM number,
/// which Windows can change (HW-2). Rewind.Register implements it with WMI (ARCH-4).
/// </summary>
public interface IDeviceLocator
{
    /// <summary>
    /// Returns the COM port, such as COM3, of the plugged-in device with these IDs, or null
    /// when none is plugged in.
    /// </summary>
    /// <param name="vendorId">The USB vendor ID: four hex digits, such as 0483.</param>
    /// <param name="productId">The USB product ID: four hex digits, such as 374B.</param>
    string? FindSerialPort(string vendorId, string productId);
}
