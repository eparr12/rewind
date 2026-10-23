using System.Threading.Channels;
using Rewind.Core.Devices;

namespace Rewind.Core.Tests;

/// <summary>
/// A scanner that's never plugged in: every search finds nothing, or throws
/// <see cref="Failure"/> when it's set (TEST-5).
/// </summary>
internal sealed class FakeDeviceLocator : IDeviceLocator
{
    private readonly Channel<int> _searches = Channel.CreateUnbounded<int>();
    private int _searchCount;

    /// <summary>What every search throws, or null to find nothing.</summary>
    public Exception? Failure { get; set; }

    public string? FindSerialPort(string vendorId, string productId)
    {
        _searches.Writer.TryWrite(Interlocked.Increment(ref _searchCount));
        if (Failure is not null)
        {
            throw Failure;
        }

        return null;
    }

    /// <summary>Waits for the next search and returns its number, starting at 1.</summary>
    public ValueTask<int> NextSearchAsync(CancellationToken cancellationToken) =>
        _searches.Reader.ReadAsync(cancellationToken);
}
