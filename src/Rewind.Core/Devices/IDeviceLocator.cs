namespace Rewind.Core.Devices;

/// <summary>
/// Finds a USB serial device's COM port from its USB IDs, never from a saved COM number,
/// which Windows can change (HW-2). Rewind.Register implements it with WMI (ARCH-4).
/// </summary>
public interface IDeviceLocator
{
    /// <summary>
    /// Returns the plugged-in device with these IDs, with its COM port, such as COM3, or null
    /// when none is plugged in.
    /// </summary>
    /// <param name="vendorId">The USB vendor ID: four hex digits, such as 0483.</param>
    /// <param name="productId">The USB product ID: four hex digits, such as 374B.</param>
    SerialDevice? FindSerialDevice(string vendorId, string productId);

    /// <summary>
    /// Whether a device that <see cref="FindSerialDevice"/> found is still plugged in. Its port
    /// can't always tell: the TMSL-77's driver keeps the port open after an unplug, so that a
    /// replugged scanner can carry on, and reads from it just find nothing. A check that fails,
    /// rather than finding the device gone, returns true, so it never drops a working scanner.
    /// </summary>
    /// <param name="deviceId">The device's <see cref="SerialDevice.DeviceId"/>.</param>
    bool IsPluggedIn(string deviceId);
}
