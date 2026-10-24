using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rewind.Core.Metadata;

namespace Rewind.Core.Tests.Metadata;

public sealed class PostersTests
{
    private static readonly byte[] Poster = [0xFF, 0xD8, 0xFF, 0xE0, 0x03, 0x42];
    private static readonly byte[] Thumb = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x54];

    // SEC-5: named for the TMDB ID, never the title.
    [Fact]
    public void FileNameFor_TmdbId_IsTheIdDotJpg() =>
        Assert.Equal("948.jpg", Posters.FileNameFor(948));

    // Wall sheet R6: data holds posters and thumbs side by side.
    [Fact]
    public async Task SaveAsync_RegisterAndThumb_SavesEachInItsFolder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        var posters = PostersIn(rewind);

        await posters.SaveAsync(948, PosterSize.Register, Poster, ct);
        await posters.SaveAsync(948, PosterSize.Thumb, Thumb, ct);

        Assert.Equal(Poster, await File.ReadAllBytesAsync(Path.Combine(rewind.Paths.Data, "posters", "948.jpg"), ct));
        Assert.Equal(Thumb, await File.ReadAllBytesAsync(Path.Combine(rewind.Paths.Data, "thumbs", "948.jpg"), ct));
    }

    [Fact]
    public async Task SaveAsync_PosterSavedBefore_ReplacesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        var posters = PostersIn(rewind);
        await posters.SaveAsync(948, PosterSize.Register, Thumb, ct);

        await posters.SaveAsync(948, PosterSize.Register, Poster, ct);

        Assert.Equal(Poster, await posters.ReadAsync(948, PosterSize.Register, ct));
        Assert.Single(Directory.GetFiles(rewind.Paths.Posters)); // nothing left over from the write
    }

    // A save that doesn't finish, such as when the clerk leaves the screen, leaves no file behind.
    [Fact]
    public async Task SaveAsync_Cancelled_LeavesNoFile()
    {
        await using var rewind = new TempRewind();
        var posters = PostersIn(rewind);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => posters.SaveAsync(948, PosterSize.Register, Poster, cancelled.Token));

        Assert.Empty(Directory.GetFiles(rewind.Paths.Posters));
    }

    // Such as after a restore, before the posters are downloaded again.
    [Fact]
    public async Task ReadAsync_NoPosterSaved_ReturnsNull()
    {
        await using var rewind = new TempRewind();

        Assert.Null(await PostersIn(rewind).ReadAsync(948, PosterSize.Register, TestContext.Current.CancellationToken));
    }

    // GEN-7: a poster that can't be read, such as one the Counter account isn't allowed to open,
    // leaves the movie without its poster, and the log says why (OBS-4).
    [Fact]
    public async Task ReadAsync_PosterCantBeRead_ReturnsNullAndWarns()
    {
        await using var rewind = new TempRewind();
        var log = new ListLogger<Posters>();
        var posters = PostersIn(rewind, log);

        // A folder where the poster should be: reading it is refused, on Windows and on Linux.
        Directory.CreateDirectory(Path.Combine(rewind.Paths.Posters, "948.jpg"));

        var poster = await posters.ReadAsync(948, PosterSize.Register, TestContext.Current.CancellationToken);

        Assert.Null(poster);
        Assert.Equal(LogLevel.Warning, Assert.Single(log.Entries).Level);
    }

    // The picker's posters are only ever shown, so there's no folder for them.
    [Fact]
    public async Task SaveAsync_PickerSize_Throws()
    {
        await using var rewind = new TempRewind();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => PostersIn(rewind).SaveAsync(948, PosterSize.Picker, Poster, TestContext.Current.CancellationToken));
    }

    private static Posters PostersIn(TempRewind rewind, ILogger<Posters>? logger = null) =>
        new(Options.Create(rewind.Paths), logger ?? NullLogger<Posters>.Instance);
}
