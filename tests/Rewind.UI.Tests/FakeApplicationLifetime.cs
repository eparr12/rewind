using Microsoft.Extensions.Hosting;

namespace Rewind.UI.Tests;

/// <summary>Records whether a screen asked Rewind to stop, instead of stopping a host.</summary>
internal sealed class FakeApplicationLifetime : IHostApplicationLifetime
{
    public bool StopRequested { get; private set; }

    public CancellationToken ApplicationStarted => CancellationToken.None;

    public CancellationToken ApplicationStopping => CancellationToken.None;

    public CancellationToken ApplicationStopped => CancellationToken.None;

    public void StopApplication() => StopRequested = true;
}
