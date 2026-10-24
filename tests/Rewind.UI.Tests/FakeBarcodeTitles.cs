using Rewind.Core.Metadata;

namespace Rewind.UI.Tests;

/// <summary>
/// The barcode lookup's Core service as the screen tests set it up (ADR-0003): it answers with
/// whatever the test gives it, which can be a task that hasn't finished, and records what it was
/// asked. A cancelled lookup ends at once, as the real one does.
/// </summary>
internal sealed class FakeBarcodeTitles : IBarcodeTitles
{
    private readonly List<string> _lookups = [];

    /// <summary>What every lookup answers. Until a test says otherwise, UPCitemdb doesn't know the barcode.</summary>
    public Task<BarcodeTitle>? Lookup { get; set; }

    /// <summary>The codes looked up, oldest first.</summary>
    public IReadOnlyList<string> Lookups => _lookups;

    /// <summary>The cancellation token the last lookup was given.</summary>
    public CancellationToken LastToken { get; private set; }

    /// <summary>Where the last lookup says it's waiting, as the pacer would.</summary>
    public IProgress<TimeSpan>? LastWaiting { get; private set; }

    public Task<BarcodeTitle> LookupAsync(string code, IProgress<TimeSpan>? waiting, CancellationToken cancellationToken)
    {
        _lookups.Add(code);
        LastToken = cancellationToken;
        LastWaiting = waiting;
        var answer = Lookup ?? Task.FromResult(new BarcodeTitle(code, UpcStatus.NotFound, null, null, null, null));
        return answer.WaitAsync(cancellationToken);
    }
}
