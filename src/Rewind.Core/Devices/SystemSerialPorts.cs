using System.IO.Ports;
using Microsoft.Extensions.Logging;

namespace Rewind.Core.Devices;

/// <summary>The PC's real serial ports, through System.IO.Ports.</summary>
public sealed partial class SystemSerialPorts(ILogger<SystemSerialPorts> logger) : ISerialPorts
{
    private const int ReadTimeoutMilliseconds = 1000;

    /// <inheritdoc />
    public ISerialPort Open(string portName, int baudRate)
    {
        var port = new SerialPort(portName, baudRate)
        {
            // Terminals such as PuTTY raise DTR and RTS when they open a port, but SerialPort
            // doesn't, and some USB serial devices stay silent until both are up.
            DtrEnable = true,
            RtsEnable = true,

            // ReadWaiting only ever asks for bytes that BytesToRead says have arrived, so Read
            // never has to wait. The timeout guards against a driver whose count is wrong: a Read
            // left waiting throws TimeoutException, which the reader handles as a lost scanner,
            // so it reconnects instead of hanging while the status bar says ready.
            ReadTimeout = ReadTimeoutMilliseconds,
        };

        try
        {
            port.Open();
            return new OpenPort(port, port.BaseStream, logger);
        }
        catch
        {
            port.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public bool IsListed(string portName) =>
        SerialPort.GetPortNames().Contains(portName, StringComparer.OrdinalIgnoreCase);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Port} threw while closing after an unplug; it's closed anyway")]
    private static partial void LogReleasedAfterUnplug(ILogger logger, Exception exception, string port);

    // Keeps the stream from Open, since after an unplug that's the only way to release the port.
    private sealed class OpenPort(SerialPort port, Stream stream, ILogger logger) : ISerialPort
    {
        public string PortName => port.PortName;

        public int ReadWaiting(byte[] buffer)
        {
            var waiting = port.BytesToRead;
            return waiting == 0 ? 0 : port.Read(buffer, 0, Math.Min(waiting, buffer.Length));
        }

        // After an unplug, .NET marks the port closed without releasing it, so SerialPort.Dispose
        // skips it and BaseStream can't be fetched any more. Disposing the stream kept from Open
        // releases the port now, rather than whenever the garbage collector runs. Its finally
        // always closes the handle, but once the device is gone it also throws.
        public void Dispose()
        {
            try
            {
                stream.Dispose();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LogReleasedAfterUnplug(logger, e, port.PortName);
            }

            port.Dispose();
        }
    }
}
