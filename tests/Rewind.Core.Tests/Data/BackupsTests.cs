namespace Rewind.Core.Tests.Data;

public sealed class BackupsTests
{
    [Fact]
    public async Task CreateAsync_OnMovieNight_NamesFileForLocalTime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var backup = await rewind.Backups.CreateAsync(ct);

        Assert.Equal("rewind-2026-10-23-2215.db", Path.GetFileName(backup));
    }

    [Fact]
    public async Task CreateAsync_TwiceInOneMinute_KeepsTheFirstBackup()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var first = await rewind.Backups.CreateAsync(ct);

        var second = await rewind.Backups.CreateAsync(ct);

        Assert.Equal("rewind-2026-10-23-2215-2.db", Path.GetFileName(second));
        Assert.True(File.Exists(first));
    }

    [Fact]
    public async Task CreateAsync_NoDatabaseYet_ReturnsNullWithoutCreatingOne()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();

        var backup = await rewind.Backups.CreateAsync(ct);

        Assert.Null(backup);
        Assert.False(File.Exists(rewind.Paths.Database));
    }
}
