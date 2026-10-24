namespace Rewind.Core.Devices;

/// <summary>A plugged-in USB serial device, as <see cref="IDeviceLocator"/> found it.</summary>
/// <param name="PortName">Its COM port, such as COM3.</param>
/// <param name="DeviceId">
/// Its Plug and Play device ID, which Device Manager shows on the Details tab as the Device
/// instance path, such as USB\VID_0483&amp;PID_374B&amp;MI_02\6&amp;…
/// </param>
public sealed record SerialDevice(string PortName, string DeviceId);
