namespace Rewind.Core.Tests;

public sealed class TempRewindTests
{
    // TEST-5: the store and the tests share one PC and one USB backup drive, so no test may look at the real drives. A
    // service the host builds gets the test's own, as it gets the test's own clock. This only asks which list the host
    // hands out, so it never lists a drive, even if the host had the real ones.
    [Fact]
    public async Task StartAsync_AnyTest_GivesTheHostTheTestsOwnDrives()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();

        await rewind.StartAsync(ct);

        Assert.Same(rewind.Drives, rewind.HostDrives);
    }
}
