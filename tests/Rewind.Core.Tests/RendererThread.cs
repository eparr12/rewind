using System.Collections.Concurrent;

namespace Rewind.Core.Tests;

/// <summary>
/// The renderer's thread on the register, for tests (ADR-0011): a SynchronizationContext whose work
/// waits until the test runs it. Core never uses ConfigureAwait(false), so a call started here comes
/// back here after each wait, and its next step waits its turn, as it would behind a key the clerk
/// pressed, until <see cref="RunQueued"/>.
/// </summary>
internal sealed class RendererThread : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queued = new();

    /// <summary>Starts the call here, and returns its task once the call first waits, with its next step left here.</summary>
    public Task Start(Func<Task> call)
    {
        var prior = Current;
        SetSynchronizationContext(this);
        try
        {
            return call();
        }
        finally
        {
            SetSynchronizationContext(prior);
        }
    }

    /// <summary>Runs what's waiting here, in turn, as the renderer would, until nothing is.</summary>
    public void RunQueued()
    {
        var prior = Current;
        SetSynchronizationContext(this);
        try
        {
            while (_queued.TryDequeue(out var work))
            {
                work.Callback(work.State);
            }
        }
        finally
        {
            SetSynchronizationContext(prior);
        }
    }

    public override void Post(SendOrPostCallback d, object? state) => _queued.Enqueue((d, state));
}
