using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rewind.Core.Tests;

/// <summary>
/// Cancels the call as soon as a read has every row, so whatever the call does after its read, such as writing the
/// phone catalog's files or reading a search's thumbnails, is what has to hear the cancel (NET-7).
/// </summary>
internal sealed class CancelAfterTheRead(CancellationTokenSource cancel) : DbCommandInterceptor
{
    public override async ValueTask<InterceptionResult> DataReaderClosingAsync(
        DbCommand command,
        DataReaderClosingEventData eventData,
        InterceptionResult result)
    {
        await cancel.CancelAsync();
        return result;
    }
}
