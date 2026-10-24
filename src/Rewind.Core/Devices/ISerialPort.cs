namespace Rewind.Core.Devices;

/// <summary>A serial port that <see cref="ISerialPorts.Open"/> opened. Disposing it closes it.</summary>
public interface ISerialPort : IDisposable
{
    /// <summary>The port, such as COM3.</summary>
    string PortName { get; }

    /// <summary>
    /// Copies the bytes that have arrived into the buffer, as many as fit, and returns how many:
    /// 0 when none have, since it never waits for more. Once the port has gone, it throws
    /// IOException, UnauthorizedAccessException, InvalidOperationException or TimeoutException.
    /// </summary>
    /// <param name="buffer">Where the bytes go.</param>
    int ReadWaiting(byte[] buffer);
}
