using System.Threading.Channels;
using Rewind.Core.Devices;

namespace Rewind.Core.Tests;

/// <summary>
/// The search for the scanner, as each test sets it up: every search finds a device on
/// <see cref="Port"/>, which is null for nothing, or throws <see cref="Failure"/> when that's
/// set (TEST-5). The device counts as plugged in while <see cref="Port"/> is set.
/// </summary>
internal sealed class FakeDeviceLocator : IDeviceLocator
{
    private const string DeviceId = @"USB\VID_0483&PID_374B\FAKE";

    private readonly Channel<int> _searches = Channel.CreateUnbounded<int>();
    private int _searchCount;

    /// <summary>The port every search finds a device on, or null to find nothing.</summary>
    public string? Port { get; set; }

    /// <summary>What every search throws, or null to return <see cref="Port"/>.</summary>
    public Exception? Failure { get; set; }

    public SerialDevice? FindSerialDevice(string vendorId, string productId)
    {
        _searches.Writer.TryWrite(Interlocked.Increment(ref _searchCount));
        if (Failure is not null)
        {
            throw Failure;
        }

        return Port is null ? null : new SerialDevice(Port, DeviceId);
    }

    public bool IsPluggedIn(string deviceId) => Port is not null;

    /// <summary>Waits for the next search and returns its number, starting at 1.</summary>
    public ValueTask<int> NextSearchAsync(CancellationToken cancellationToken) =>
        _searches.Reader.ReadAsync(cancellationToken);
}
