using Microsoft.EntityFrameworkCore;
using Rewind.Core.Catalog;
using Rewind.Core.Data;

namespace Rewind.Core.Tests;

public sealed class BackwardsReadsTests
{
    // TEST-4: a test that leans on BackwardsReads still passes if its pragma stops applying, and the order
    // mutations it catches then survive unseen, so this holds the pragma itself to its job.
    [Fact]
    public async Task CreateDbContext_UnsortedReadOfTwoMovies_ComesBackInReverse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using (var db = rewind.OpenDb())
        {
            db.Movies.AddRange(new Movie { Title = "Halloween" }, new Movie { Title = "Scream" });
            await db.SaveChangesAsync(ct);
        }

        var usual = await UnsortedIdsAsync(rewind.DbFactory, ct);
        var backwards = await UnsortedIdsAsync(new BackwardsReads(rewind), ct);

        Assert.Equal(usual.AsEnumerable().Reverse(), backwards);
    }

    // No OrderBy, so the ids come back in whatever order SQLite reads them.
    private static async Task<List<int>> UnsortedIdsAsync(IDbContextFactory<RewindDb> contexts, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Movies.Select(m => m.Id).ToListAsync(cancellationToken);
    }
}
