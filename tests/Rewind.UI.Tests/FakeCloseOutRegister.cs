using Rewind.Core.CloseOut;
using Rewind.Core.MovieNights;

namespace Rewind.UI.Tests;

/// <summary>
/// Close Out's Core service as the Store Manager's tests set it up (ADR-0003). Each Close Out answers with the report
/// of a movie night where everything worked, until a test gives it an answer of its own, which can be a task that
/// hasn't finished, or one that fails or is cancelled. It counts the Close Outs it was asked for, and keeps the token
/// the last one was given.
/// </summary>
internal sealed class FakeCloseOutRegister : ICloseOutRegister
{
    /// <summary>
    /// A movie night closed, its backup saved, a catalog of 120 movies written, and that backup copied to the backup
    /// drive E:\, which had the rest.
    /// </summary>
    public static readonly CloseOutReport EverythingWorked = new(
        BusinessDayClosingStatus.Closed,
        "rewind-2026-10-23-2215.db",
        120,
        new DriveCopy(DriveCopyStatus.Copied, 1, [@"E:\"], []));

    /// <summary>The same night with no backup drive plugged in: the Build Plan's "a missing USB drive".</summary>
    public static readonly CloseOutReport NoDrive = EverythingWorked with { Copy = new DriveCopy(DriveCopyStatus.NoDrive, 0, [], []) };

    /// <summary>What every Close Out answers.</summary>
    public Task<CloseOutReport> Running { get; set; } = Task.FromResult(EverythingWorked);

    /// <summary>How many Close Outs the screen asked for.</summary>
    public int Runs { get; private set; }

    /// <summary>The token the last Close Out was given.</summary>
    public CancellationToken LastToken { get; private set; }

    public Task<CloseOutReport> RunAsync(CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        Runs++;
        return Running;
    }
}
