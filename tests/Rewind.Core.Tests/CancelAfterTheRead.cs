using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rewind.Core.Tests;

/// <summary>
/// Cancels the call as soon as a read has every row, so whatever the call does after its read, such as writing the
/// phone catalog's files or reading a search's thumbnails, is what has to hear the cancel (NET-7). For a call that
/// reads several times, it can wait for a later read instead, so the read after that one is what has to hear it. One
/// interceptor counts the reads of every context it's given to.
/// </summary>
internal sealed class CancelAfterTheRead(CancellationTokenSource cancel, int reads = 1) : DbCommandInterceptor
{
    private int _readsDone;

    public override async ValueTask<InterceptionResult> DataReaderClosingAsync(
        DbCommand command,
        DataReaderClosingEventData eventData,
        InterceptionResult result)
    {
        if (Interlocked.Increment(ref _readsDone) >= reads)
        {
            await cancel.CancelAsync();
        }

        return result;
    }
}
