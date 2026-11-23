using Microsoft.EntityFrameworkCore;
using Rewind.Core.Data;

namespace Rewind.Core.Tests;

public sealed class CancelAfterTheReadTests
{
    // NET-7: a test that waits for a later read still passes if the cancel comes after the first one instead, since
    // every read of a call hears the token, so this holds the count to its job. The two reads are two contexts', as a
    // service's reads often are, since one interceptor serves every context it's given to.
    [Fact]
    public async Task DataReaderClosingAsync_SecondReadAskedFor_CancelsOnlyOnceItHasEveryRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        using var cancel = new CancellationTokenSource();
        var contexts = rewind.ContextsWith(new CancelAfterTheRead(cancel, reads: 2));

        await ReadTheMoviesAsync(contexts, ct);
        var cancelledAfterTheFirst = cancel.IsCancellationRequested;
        await ReadTheMoviesAsync(contexts, ct);

        Assert.Equal((false, true), (cancelledAfterTheFirst, cancel.IsCancellationRequested));
    }

    // One read, with a context of its own. The test's own token, so the cancel never stops it.
    private static async Task ReadTheMoviesAsync(IDbContextFactory<RewindDb> contexts, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.Movies.AsNoTracking().Select(m => m.Id).ToListAsync(cancellationToken);
    }
}
