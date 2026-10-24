using System.Threading.Channels;
using Rewind.Core.Devices;

namespace Rewind.Core.Tests;

/// <summary>
/// The search for the scanner, as each test sets it up: every search finds <see cref="Port"/>,
/// which is null for nothing, or throws <see cref="Failure"/> when that's set (TEST-5).
/// </summary>
internal sealed class FakeDeviceLocator : IDeviceLocator
{
    private readonly Channel<int> _searches = Channel.CreateUnbounded<int>();
    private int _searchCount;

    /// <summary>The port every search finds, or null to find nothing.</summary>
    public string? Port { get; set; }

    /// <summary>What every search throws, or null to return <see cref="Port"/>.</summary>
    public Exception? Failure { get; set; }

    public string? FindSerialPort(string vendorId, string productId)
    {
        _searches.Writer.TryWrite(Interlocked.Increment(ref _searchCount));
        if (Failure is not null)
        {
            throw Failure;
        }

        return Port;
    }

    /// <summary>Waits for the next search and returns its number, starting at 1.</summary>
    public ValueTask<int> NextSearchAsync(CancellationToken cancellationToken) =>
        _searches.Reader.ReadAsync(cancellationToken);
}
