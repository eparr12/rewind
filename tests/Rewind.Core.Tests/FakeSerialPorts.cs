using System.Collections.Concurrent;
using System.Text;
using Rewind.Core.Devices;

namespace Rewind.Core.Tests;

/// <summary>
/// The PC's serial ports, as each test sets them up (TEST-5): every port opens and stays
/// listed, and an open port reads whatever the test sends, until the test makes opening or
/// reading throw <see cref="OpenFailure"/> or <see cref="ReadFailure"/>.
/// </summary>
internal sealed class FakeSerialPorts : ISerialPorts
{
    private readonly ConcurrentQueue<byte> _received = new();

    /// <summary>What opening a port throws, or null for it to open.</summary>
    public Exception? OpenFailure { get; set; }

    /// <summary>What reading an open port throws, or null for it to read what was sent.</summary>
    public Exception? ReadFailure { get; set; }

    /// <summary>Whether Windows lists every port.</summary>
    public bool Listed { get; set; } = true;

    public ISerialPort Open(string portName, int baudRate) =>
        OpenFailure is null ? new Port(this, portName) : throw OpenFailure;

    public bool IsListed(string portName) => Listed;

    /// <summary>Has the scanner send this text, as ASCII.</summary>
    public void Send(string text)
    {
        foreach (var b in Encoding.ASCII.GetBytes(text))
        {
            _received.Enqueue(b);
        }
    }

    private sealed class Port(FakeSerialPorts ports, string portName) : ISerialPort
    {
        public string PortName => portName;

        public int ReadWaiting(byte[] buffer)
        {
            if (ports.ReadFailure is { } failure)
            {
                throw failure;
            }

            var count = 0;
            while (count < buffer.Length && ports._received.TryDequeue(out var b))
            {
                buffer[count++] = b;
            }

            return count;
        }

        public void Dispose()
        {
        }
    }
}
