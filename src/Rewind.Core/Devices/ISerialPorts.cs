namespace Rewind.Core.Devices;

/// <summary>
/// The PC's serial ports, as <see cref="SerialScanner"/> uses them, so tests can stand in for a
/// scanner that's plugged in, unplugged or held by another program (TEST-5).
/// <see cref="SystemSerialPorts"/> is the real one.
/// </summary>
public interface ISerialPorts
{
    /// <summary>
    /// Opens a port at this speed, 8N1. Throws UnauthorizedAccessException when another program
    /// has the port open, and IOException when the port has gone, usually in an unplug.
    /// </summary>
    /// <param name="portName">The port, such as COM3.</param>
    /// <param name="baudRate">The speed, such as 115200 (HW-1).</param>
    ISerialPort Open(string portName, int baudRate);

    /// <summary>Whether the port is in Windows' list of the serial ports that exist right now.</summary>
    /// <param name="portName">The port, such as COM3.</param>
    bool IsListed(string portName);
}
