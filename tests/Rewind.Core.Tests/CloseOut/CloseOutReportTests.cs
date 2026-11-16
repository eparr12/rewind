using Rewind.Core.CloseOut;
using Rewind.Core.MovieNights;

namespace Rewind.Core.Tests.CloseOut;

public sealed class CloseOutReportTests
{
    // Every step done, with the copy answering Copied.
    private static readonly CloseOutReport Done = new(BusinessDayClosingStatus.Closed, "rewind-2026-10-23-2215.db", 1, CopyThat(DriveCopyStatus.Copied));

    // The one place the checklist reads whether everything worked: each backup drive has every backup after Copied or
    // NothingToCopy, and doesn't after the other two.
    [Theory]
    [InlineData(DriveCopyStatus.Copied, true)]
    [InlineData(DriveCopyStatus.NothingToCopy, true)]
    [InlineData(DriveCopyStatus.NoDrive, false)]
    [InlineData(DriveCopyStatus.Unwritable, false)]
    public void EverythingWorked_BackupAndCatalogDone_GoesByWhatTheCopyAnswered(DriveCopyStatus status, bool worked)
    {
        var report = Done with { Copy = CopyThat(status) };

        Assert.Equal(worked, report.EverythingWorked);
    }

    [Fact]
    public void EverythingWorked_BackupNotSaved_IsFalse()
    {
        var report = Done with { Backup = null };

        Assert.False(report.EverythingWorked);
    }

    [Fact]
    public void EverythingWorked_CatalogNotWritten_IsFalse()
    {
        var report = Done with { CatalogMovies = null };

        Assert.False(report.EverythingWorked);
    }

    [Fact]
    public void EverythingWorked_CopyCouldntBeTried_IsFalse()
    {
        var report = Done with { Copy = null };

        Assert.False(report.EverythingWorked);
    }

    // A night of returns only, or a second Close Out the same night, has no night to close, and that's no failure.
    [Fact]
    public void EverythingWorked_NoNightOpen_IsStillTrue()
    {
        var report = Done with { Night = BusinessDayClosingStatus.NothingOpen };

        Assert.True(report.EverythingWorked);
    }

    // An empty collection writes a catalog of no movies, which is a catalog written.
    [Fact]
    public void EverythingWorked_CatalogOfNoMovies_IsStillTrue()
    {
        var report = Done with { CatalogMovies = 0 };

        Assert.True(report.EverythingWorked);
    }

    private static DriveCopy CopyThat(DriveCopyStatus status) => new(status, 0, [], []);
}
