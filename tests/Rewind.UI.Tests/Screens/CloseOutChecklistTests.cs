using System.Diagnostics;
using System.Globalization;
using Rewind.Core.CloseOut;
using Rewind.Core.MovieNights;
using Rewind.UI.Dialogs;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class CloseOutChecklistTests
{
    private const string NotEverythingWorked =
        "Close Out is done, but not everything worked. Each line without a tick says what to do.";

    private static readonly CloseOutReport EverythingWorked = FakeCloseOutRegister.EverythingWorked;

    // Build Plan 3.5: one line for each step, in order, each ticked, and Info, which plays nothing.
    [Fact]
    public void For_EverythingWorked_TicksEveryLineInOrder()
    {
        var checklist = CloseOutChecklist.For(EverythingWorked);

        Assert.Equal(
            (Store.Name, "Close Out is done, and everything worked.", DialogIcon.Info, false),
            (checklist.Title, checklist.Message, checklist.Icon, checklist.CanCancel));
        Assert.Equal(
            [
                new ChecklistLine("The movie night is closed.", Ticked: true),
                new ChecklistLine("The backup rewind-2026-10-23-2215.db is saved.", Ticked: true),
                new ChecklistLine("The phone catalog is written, with 120 movies.", Ticked: true),
                new ChecklistLine(@"Copied 1 backup to the backup drive E:\.", Ticked: true),
            ],
            checklist.Checklist);
    }

    // ADR-0013: a night of returns only, or a second Close Out the same night, has no night to close, and that's no
    // problem, so the line is ticked and the checklist stays Info.
    [Fact]
    public void For_NoNightOpen_TicksTheNightAndStaysInfo()
    {
        var checklist = CloseOutChecklist.For(EverythingWorked with { Night = BusinessDayClosingStatus.NothingOpen });

        Assert.Equal(new ChecklistLine("No movie night was open, so there was nothing to close.", Ticked: true), checklist.Checklist[0]);
        Assert.Equal(DialogIcon.Info, checklist.Icon);
    }

    // A drive left at home gets every backup it missed the next time (ADR-0013), so one drive can take several.
    [Fact]
    public void For_SeveralBackupsToOneDrive_SaysHowMany()
    {
        var copy = new DriveCopy(DriveCopyStatus.Copied, 3, [@"E:\"], []);

        var checklist = CloseOutChecklist.For(EverythingWorked with { Copy = copy });

        Assert.Equal(new ChecklistLine(@"Copied 3 backups to the backup drive E:\.", Ticked: true), checklist.Checklist[3]);
        Assert.Equal(DialogIcon.Info, checklist.Icon);
    }

    // Files counts each copy, so with more than one drive the number is a total: one backup to two drives is two.
    [Theory]
    [InlineData(2, @"Copied 2 backups in all to the backup drives E:\ and F:\.", @"E:\", @"F:\")]
    [InlineData(6, @"Copied 6 backups in all to the backup drives E:\, F:\ and G:\.", @"E:\", @"F:\", @"G:\")]
    public void For_BackupsToSeveralDrives_SaysHowManyInAll(int files, string line, params string[] drives)
    {
        var copy = new DriveCopy(DriveCopyStatus.Copied, files, drives, []);

        var checklist = CloseOutChecklist.For(EverythingWorked with { Copy = copy });

        Assert.Equal(new ChecklistLine(line, Ticked: true), checklist.Checklist[3]);
    }

    // A second Close Out the same night finds the drive up to date, and that worked too.
    [Theory]
    [InlineData(@"The backup drive E:\ already had every backup.", @"E:\")]
    [InlineData(@"The backup drives E:\ and F:\ already had every backup.", @"E:\", @"F:\")]
    public void For_DriveUpToDate_SaysItHadEveryBackup(string line, params string[] drives)
    {
        var copy = new DriveCopy(DriveCopyStatus.NothingToCopy, 0, drives, []);

        var checklist = CloseOutChecklist.For(EverythingWorked with { Copy = copy });

        Assert.Equal(new ChecklistLine(line, Ticked: true), checklist.Checklist[3]);
        Assert.Equal(DialogIcon.Info, checklist.Icon);
    }

    [Fact]
    public void For_BackupNotSaved_UnticksItAndSaysToTellTheStoreManager()
    {
        var checklist = CloseOutChecklist.For(EverythingWorked with { Backup = null });

        Assert.Equal(new ChecklistLine("The backup wasn't saved. Tell the Store Manager.", Ticked: false), checklist.Checklist[1]);
        Assert.Equal((NotEverythingWorked, DialogIcon.Warning), (checklist.Message, checklist.Icon));
    }

    [Fact]
    public void For_CatalogNotWritten_UnticksItAndSaysToTellTheStoreManager()
    {
        var checklist = CloseOutChecklist.For(EverythingWorked with { CatalogMovies = null });

        Assert.Equal(new ChecklistLine("The phone catalog wasn't written. Tell the Store Manager.", Ticked: false), checklist.Checklist[2]);
        Assert.Equal((NotEverythingWorked, DialogIcon.Warning), (checklist.Message, checklist.Icon));
    }

    // Done when (Build Plan 3.5): a missing drive is a warning, whose line says what a kid can do about it.
    [Fact]
    public void For_NoDrive_UnticksItAndSaysToPlugTheDriveIn()
    {
        var checklist = CloseOutChecklist.For(FakeCloseOutRegister.NoDrive);

        Assert.Equal(
            new ChecklistLine("No backup drive is plugged in. Plug the backup drive in, then press Close Out Register again.", Ticked: false),
            checklist.Checklist[3]);
        Assert.Equal((NotEverythingWorked, DialogIcon.Warning), (checklist.Message, checklist.Icon));
    }

    // Only the drive that couldn't be written to is named: the other was given what it was missing.
    [Fact]
    public void For_DriveUnwritable_NamesThatDriveAndSaysToTellTheStoreManager()
    {
        var copy = new DriveCopy(DriveCopyStatus.Unwritable, 2, [@"E:\", @"F:\"], [@"F:\"]);

        var checklist = CloseOutChecklist.For(EverythingWorked with { Copy = copy });

        Assert.Equal(new ChecklistLine(@"Rewind couldn't copy to the backup drive F:\. Tell the Store Manager.", Ticked: false), checklist.Checklist[3]);
        Assert.Equal((NotEverythingWorked, DialogIcon.Warning), (checklist.Message, checklist.Icon));
    }

    [Fact]
    public void For_TwoDrivesUnwritable_NamesBoth()
    {
        var copy = new DriveCopy(DriveCopyStatus.Unwritable, 0, [@"E:\", @"F:\"], [@"E:\", @"F:\"]);

        var checklist = CloseOutChecklist.For(EverythingWorked with { Copy = copy });

        Assert.Equal(new ChecklistLine(@"Rewind couldn't copy to the backup drives E:\ and F:\. Tell the Store Manager.", Ticked: false), checklist.Checklist[3]);
    }

    // A copy that stopped on a failure that wasn't a drive's. Tonight's backup goes first, so it may be on the drive:
    // the line says the copy stopped, never that nothing was copied.
    [Fact]
    public void For_CopyStoppedPartway_UnticksItAndSaysToTellTheStoreManager()
    {
        var checklist = CloseOutChecklist.For(EverythingWorked with { Copy = null });

        Assert.Equal(new ChecklistLine("Copying to the backup drive stopped partway. Tell the Store Manager.", Ticked: false), checklist.Checklist[3]);
        Assert.Equal((NotEverythingWorked, DialogIcon.Warning), (checklist.Message, checklist.Icon));
    }

    // Each line that didn't work says so on its own, and the rest stay ticked.
    [Fact]
    public void For_NothingAfterTheNightWorked_UnticksEachOfThem()
    {
        var checklist = CloseOutChecklist.For(new CloseOutReport(BusinessDayClosingStatus.Closed, null, null, null));

        Assert.Equal([true, false, false, false], checklist.Checklist.Select(line => line.Ticked));
        Assert.Equal(DialogIcon.Warning, checklist.Icon);
    }

    [Theory]
    [InlineData(0, "The phone catalog is written, with 0 movies.")]
    [InlineData(1, "The phone catalog is written, with 1 movie.")]
    [InlineData(2, "The phone catalog is written, with 2 movies.")]
    [InlineData(1234, "The phone catalog is written, with 1,234 movies.")]
    public void For_CatalogWritten_SaysHowManyMovies(int movies, string line)
    {
        var checklist = CloseOutChecklist.For(EverythingWorked with { CatalogMovies = movies });

        Assert.Equal(new ChecklistLine(line, Ticked: true), checklist.Checklist[2]);
    }

    // The register's numbers read the same on any PC: France groups thousands with a space.
    [Fact]
    public void For_FrenchCulture_GroupsThousandsWithACommaStill()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var checklist = CloseOutChecklist.For(EverythingWorked with { CatalogMovies = 1234 });

            Assert.Equal("The phone catalog is written, with 1,234 movies.", checklist.Checklist[2].Words);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    // NET-12, OBS-5: a status Core never answers is a bug, not a line.
    [Fact]
    public void For_UnknownNight_Throws()
    {
        Assert.Throws<UnreachableException>(() => CloseOutChecklist.For(EverythingWorked with { Night = (BusinessDayClosingStatus)99 }));
    }

    [Fact]
    public void For_UnknownCopyStatus_Throws()
    {
        var copy = new DriveCopy((DriveCopyStatus)99, 0, [], []);

        Assert.Throws<UnreachableException>(() => CloseOutChecklist.For(EverythingWorked with { Copy = copy }));
    }

    // Core names every backup drive it copied to, so a copy that names none is a bug too.
    [Fact]
    public void For_CopiedToNoDrive_Throws()
    {
        var copy = new DriveCopy(DriveCopyStatus.Copied, 1, [], []);

        Assert.Throws<UnreachableException>(() => CloseOutChecklist.For(EverythingWorked with { Copy = copy }));
    }

    [Fact]
    public void For_NoReport_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CloseOutChecklist.For(null!)); // NET-1: null on purpose
    }
}
