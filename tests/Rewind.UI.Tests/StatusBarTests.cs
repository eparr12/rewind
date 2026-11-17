using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rewind.Core;
using Rewind.Core.Concessions;
using Rewind.Core.Data;
using Rewind.Core.Devices;
using Rewind.Core.Members;
using Rewind.Core.Scans;
using Rewind.UI.Errors;

namespace Rewind.UI.Tests;

public sealed class StatusBarTests : BunitContext
{
    private static readonly MemberRow Grandma = new(1, "Grandma", "MEM-001", IsActive: true);
    private static readonly MemberRow Dad = new(2, "Dad", "MEM-002", IsActive: false);
    private static readonly ConcessionRow Popcorn = new(1, "Popcorn", "CON-POPCORN", 100, IsActive: true);
    private static readonly ConcessionRow Candy = new(2, "Candy", "CON-CANDY", 50, IsActive: false);

    // 10:15 PM on Friday, October 23, 2026, as a backup made then is named: rewind-2026-10-23-2215.db.
    private static readonly DateTime FridayNight = new(2026, 10, 23, 22, 15, 0);

    private readonly FakeScanner _scanner = new();
    private readonly FakeMemberRoster _roster = new();
    private readonly FakeConcessionBar _bar = new();
    private readonly FakeLastBackup _lastBackup = new();
    private readonly ListLogger<ErrorHub> _errorLog = new();
    private readonly RewindPaths _paths = new();
    private bool _crashed;

    public StatusBarTests()
    {
        var errors = new ErrorHub(_errorLog);
        errors.Crashed += () => _crashed = true;
        Services.AddSingleton<IScanner>(_scanner);
        Services.AddSingleton<IMemberRoster>(_roster);
        Services.AddSingleton<IConcessionBar>(_bar);
        Services.AddSingleton<ILastBackup>(_lastBackup);
        Services.AddSingleton(errors);
        Services.AddSingleton(Options.Create(_paths));
    }

    // The register's bar stays as it was, so the field only ever means something's wrong.
    [Theory]
    [InlineData(@"C:\Rewind")]
    [InlineData(@"c:/rewind/")]
    public void Render_TheRegistersRoot_ShowsNoRoot(string root)
    {
        _paths.Root = root;

        var cut = Render<StatusBar>();

        Assert.DoesNotContain("Data:", cut.Find(".status-bar").TextContent, StringComparison.Ordinal);
    }

    // A Release build started from a window with Paths__Root set runs movie night on that folder.
    [Fact]
    public void Render_AnotherRoot_ShowsIt()
    {
        _paths.Root = @"C:\RewindDev";

        var cut = Render<StatusBar>();

        Assert.Contains(@"Data: C:\RewindDev", cut.FindAll(".status-bar-field").Select(field => field.TextContent));
    }

    [Fact]
    public void Render_ScannerNotFoundYet_ShowsScannerMissing()
    {
        var cut = Render<StatusBar>();

        Assert.Contains("Scanner missing", cut.Find(".status-bar").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusChanged_ScannerFound_ShowsScannerReady()
    {
        var cut = Render<StatusBar>();

        _scanner.SetStatus(ScannerStatus.Ready);

        cut.WaitForAssertion(() =>
            Assert.Contains("Scanner ready", cut.Find(".status-bar").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Scanned_StoreLabel_ShowsCodeAndKind()
    {
        var cut = Render<StatusBar>();

        _scanner.Emit(new Scan(ScanKind.StoreLabel, "RV-00042"));

        cut.WaitForAssertion(() =>
            Assert.Contains("RV-00042 · Store label", cut.Find(".status-bar").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Scanned_TooLongToKeep_ShowsUnknownBarcode()
    {
        var cut = Render<StatusBar>();

        _scanner.Emit(new Scan(ScanKind.Unknown, ""));

        cut.WaitForAssertion(() =>
            Assert.Contains("Unknown barcode", cut.Find(".status-bar").TextContent, StringComparison.Ordinal));
    }

    // Build Plan 3.1: the status bar names each card when it's scanned.
    [Fact]
    public async Task Scanned_MemberCard_ShowsTheMembersName()
    {
        _roster.Rows.AddRange([Grandma, Dad]);
        var cut = Render<StatusBar>();

        await ScanAsync(cut, "MEM-001");

        Assert.Equal("MEM-001 · Grandma", LastScan(cut));
    }

    // A deactivated member keeps their card (DATA-4), so it still names them.
    [Fact]
    public async Task Scanned_CardOfAnInactiveMember_ShowsTheirName()
    {
        _roster.Rows.Add(Dad);
        var cut = Render<StatusBar>();

        await ScanAsync(cut, "MEM-002");

        Assert.Equal("MEM-002 · Dad", LastScan(cut));
    }

    // A card nobody has yet, such as one the Members tab is about to add, shows as it did before.
    [Fact]
    public async Task Scanned_CardNobodyHas_ShowsMemberCard()
    {
        _roster.Rows.Add(Grandma);
        var cut = Render<StatusBar>();

        await ScanAsync(cut, "MEM-011");

        Assert.Equal("MEM-011 · Member card", LastScan(cut));
    }

    // On the register, SQLite has answered before the bar draws, so the bar draws once, with the name.
    // Drawing first would flash "Member card" before it.
    [Fact]
    public async Task Scanned_MemberCardAnsweredAtOnce_DrawsTheScanOnce()
    {
        _roster.Rows.Add(Grandma);
        var cut = Render<StatusBar>();
        var renders = cut.RenderCount;

        await ScanAsync(cut, "MEM-001");

        Assert.Equal(renders + 1, cut.RenderCount);
    }

    // A lookup still on its way shows the scan as a member card meanwhile, without the last scan's member.
    [Fact]
    public async Task Scanned_MemberCardBeforeTheRosterAnswers_ShowsMemberCard()
    {
        _roster.Rows.Add(Grandma);
        var cut = Render<StatusBar>();
        await ScanAsync(cut, "MEM-001");
        _roster.Finding = new TaskCompletionSource<MemberRow?>().Task;

        await ScanAsync(cut, "MEM-002");

        Assert.Equal("MEM-002 · Member card", LastScan(cut));
    }

    // An answer that comes after the next scan is about a card that isn't showing any more.
    [Fact]
    public async Task Scanned_AnswerComesAfterTheNextScan_KeepsShowingTheNextScan()
    {
        _roster.Rows.Add(Dad);
        var first = new TaskCompletionSource<MemberRow?>();
        _roster.Finding = first.Task;
        var cut = Render<StatusBar>();
        await ScanAsync(cut, "MEM-001");
        _roster.Finding = null;
        await ScanAsync(cut, "MEM-002");

        first.SetResult(Grandma);
        await cut.InvokeAsync(() => { }); // queued behind the bar's own continuation

        Assert.Equal("MEM-002 · Dad", LastScan(cut));
    }

    // A card scanned again is a new scan, so the answer for the first one comes too late, even when it
    // comes last.
    [Fact]
    public async Task Scanned_SameCardAgainBeforeTheFirstAnswer_ShowsTheSecondAnswer()
    {
        var first = new TaskCompletionSource<MemberRow?>();
        _roster.Finding = first.Task;
        var cut = Render<StatusBar>();
        await ScanAsync(cut, "MEM-001");
        _roster.Finding = Task.FromResult<MemberRow?>(Grandma);
        await ScanAsync(cut, "MEM-001");

        first.SetResult(Grandma with { Name = "Granma" }); // as she was before a rename
        await cut.InvokeAsync(() => { }); // queued behind the bar's own continuation

        Assert.Equal("MEM-001 · Grandma", LastScan(cut));
    }

    // Only a member card and a snack's code have names, so nothing else is looked up. Both fakes answer
    // with a name whatever they're asked, so a store label looked up by either would show one.
    [Fact]
    public async Task Scanned_NeitherACardNorASnack_ShowsNoName()
    {
        _roster.Finding = Task.FromResult<MemberRow?>(Grandma);
        _bar.Finding = Task.FromResult<ConcessionRow?>(Popcorn);
        var cut = Render<StatusBar>();

        await ScanAsync(cut, "RV-00042", ScanKind.StoreLabel);

        Assert.Equal("RV-00042 · Store label", LastScan(cut));
    }

    // Build Plan 3.1: the status bar names each snack when it's scanned, as it does each card.
    [Fact]
    public async Task Scanned_Snack_ShowsItsName()
    {
        _bar.Rows.AddRange([Popcorn, Candy]);
        var cut = Render<StatusBar>();

        await ScanAsync(cut, "CON-POPCORN", ScanKind.Concession);

        Assert.Equal("CON-POPCORN · Popcorn", LastScan(cut));
    }

    // A deactivated snack keeps its code (DATA-4), and a menu printed before may still carry it.
    [Fact]
    public async Task Scanned_DeactivatedSnack_ShowsItsName()
    {
        _bar.Rows.Add(Candy);
        var cut = Render<StatusBar>();

        await ScanAsync(cut, "CON-CANDY", ScanKind.Concession);

        Assert.Equal("CON-CANDY · Candy", LastScan(cut));
    }

    // A code no snack has yet, such as one the Snacks tab is about to add, shows as it did before.
    [Fact]
    public async Task Scanned_CodeNoSnackHas_ShowsConcession()
    {
        _bar.Rows.Add(Popcorn);
        var cut = Render<StatusBar>();

        await ScanAsync(cut, "CON-NACHOS", ScanKind.Concession);

        Assert.Equal("CON-NACHOS · Concession", LastScan(cut));
    }

    // As for a card: SQLite has answered before the bar draws, so it draws once, with the name.
    [Fact]
    public async Task Scanned_SnackAnsweredAtOnce_DrawsTheScanOnce()
    {
        _bar.Rows.Add(Popcorn);
        var cut = Render<StatusBar>();
        var renders = cut.RenderCount;

        await ScanAsync(cut, "CON-POPCORN", ScanKind.Concession);

        Assert.Equal(renders + 1, cut.RenderCount);
    }

    // A lookup still on its way shows the scan as a concession meanwhile, without the last scan's name.
    [Fact]
    public async Task Scanned_SnackBeforeTheBarAnswers_ShowsConcession()
    {
        _roster.Rows.Add(Grandma);
        var cut = Render<StatusBar>();
        await ScanAsync(cut, "MEM-001");
        _bar.Finding = new TaskCompletionSource<ConcessionRow?>().Task;

        await ScanAsync(cut, "CON-POPCORN", ScanKind.Concession);

        Assert.Equal("CON-POPCORN · Concession", LastScan(cut));
    }

    // A failed lookup goes to ErrorHub even while the bar is open, so the shell shows the error dialog
    // and carries on. The bar's ErrorBoundary would close it, and leave it closed, over a name.
    [Fact]
    public async Task Scanned_LookupFails_ReportsItToErrorHub()
    {
        _roster.Finding = Task.FromException<MemberRow?>(new InvalidOperationException("The lookup failed."));
        var cut = Render<StatusBar>();

        await ScanAsync(cut, "MEM-001");

        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.True(_crashed);
    }

    // The bar still says what was scanned last, without a name.
    [Fact]
    public async Task Scanned_LookupFails_StillShowsTheScan()
    {
        _roster.Finding = Task.FromException<MemberRow?>(new InvalidOperationException("The lookup failed."));
        var cut = Render<StatusBar>();

        await ScanAsync(cut, "MEM-001");

        Assert.Equal("MEM-001 · Member card", LastScan(cut));
    }

    // The status bar closes after an error of its own, when the shell crashes or when Rewind closes, and
    // that stops a lookup still waiting on the database.
    [Fact]
    public async Task Dispose_WhileLookingUp_CancelsTheLookup()
    {
        _roster.Finding = new TaskCompletionSource<MemberRow?>().Task;
        var cut = Render<StatusBar>();
        await ScanAsync(cut, "MEM-001");

        await DisposeComponentsAsync();

        Assert.True(_roster.LastToken.IsCancellationRequested);
    }

    // And a snack's.
    [Fact]
    public async Task Dispose_WhileLookingUpASnack_CancelsTheLookup()
    {
        _bar.Finding = new TaskCompletionSource<ConcessionRow?>().Task;
        var cut = Render<StatusBar>();
        await ScanAsync(cut, "CON-POPCORN", ScanKind.Concession);

        await DisposeComponentsAsync();

        Assert.True(_bar.LastToken.IsCancellationRequested);
    }

    // The lookup runs as a fire-and-forget task, which ComponentBase never sees, so the status bar
    // catches its own cancellation. Without the catch, the cancellation would go to ErrorHub as an error
    // after the bar closed, and the clerk would see the error dialog.
    [Fact]
    public async Task Dispose_WhileLookingUp_RaisesNothingOnceCancelled()
    {
        var finding = new TaskCompletionSource<MemberRow?>();
        _roster.Finding = finding.Task;
        var cut = Render<StatusBar>();
        await ScanAsync(cut, "MEM-001");
        await DisposeComponentsAsync();

        finding.SetCanceled(_roster.LastToken); // as EF Core ends a read whose token was cancelled
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the bar's own continuation

        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
    }

    // A lookup that fails after the status bar closed goes to ErrorHub too, which logs it, naming the
    // status bar (OBS-3), and tells whichever shell there is (OBS-5).
    [Fact]
    public async Task Dispose_WhileLookingUp_ReportsAFailureToErrorHub()
    {
        var finding = new TaskCompletionSource<MemberRow?>();
        _roster.Finding = finding.Task;
        var cut = Render<StatusBar>();
        await ScanAsync(cut, "MEM-001");
        await DisposeComponentsAsync();

        finding.SetException(new InvalidOperationException("The lookup failed."));
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the bar's own continuation

        Assert.True(_crashed);
        Assert.Equal(["Unhandled exception from StatusBar; reported to the shell"], _errorLog.Entries.Select(entry => entry.Message));
    }

    // A scan the scanner's thread raised just before the status bar closed reaches it afterwards. Its
    // token went with it, so the card isn't looked up, and nothing is raised.
    [Fact]
    public async Task Dispose_ScanRaisedJustBefore_RaisesNothing()
    {
        _ = Render<StatusBar>();
        var raisedJustBefore = _scanner.RaisedBeforeStopping();
        await DisposeComponentsAsync();

        raisedJustBefore(new Scan(ScanKind.MemberCard, "MEM-001"));
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind anything the scan started

        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
    }

    // OBS-6: the last backup's field comes right after the scanner's, before the root a Debug build shows, and
    // before the first backup it says there's none.
    [Fact]
    public void Render_NoBackupYet_SaysSoAfterTheScanner()
    {
        _paths.Root = @"C:\RewindDev";

        var cut = Render<StatusBar>();

        Assert.Equal(
            ["No scan yet", "Scanner missing", "No backup yet", @"Data: C:\RewindDev", "F1 to F5 switch screens", "Be kind, rewind."],
            Fields(cut));
    }

    // Written the way Return writes a night: the weekday and the date, as the backup's name gives them.
    [Fact]
    public void Render_BackupMadeFridayNight_ShowsItsDay()
    {
        _lastBackup.MadeAt = FridayNight;

        var cut = Render<StatusBar>();

        Assert.Equal("Last backup: Friday, October 23", LastBackupField(cut));
    }

    // A Close Out at 12:05 AM on Saturday, October 3 makes rewind-2026-10-03-0005.db, so the bar names Saturday,
    // the day in the name, though Return would name the night Friday's. The date takes one digit when it has one.
    [Fact]
    public void Render_BackupMadePastMidnight_ShowsTheDayInItsName()
    {
        _lastBackup.MadeAt = new DateTime(2026, 10, 3, 0, 5, 0);

        var cut = Render<StatusBar>();

        Assert.Equal("Last backup: Saturday, October 3", LastBackupField(cut));
    }

    // As in Return, the day is in English, as the rest of the bar is, whatever language the PC is set to.
    [Fact]
    public void Render_PcSetToFrench_StillNamesTheDayInEnglish()
    {
        _lastBackup.MadeAt = FridayNight;
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var cut = Render<StatusBar>();

            Assert.Equal("Last backup: Friday, October 23", LastBackupField(cut));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    // UI-11: a backup is made on a background thread, and the bar moves onto its own before it reads again.
    [Fact]
    public async Task BackedUp_FromAnotherThread_ShowsTheNewBackup()
    {
        var cut = Render<StatusBar>();
        _lastBackup.MadeAt = FridayNight;

        await BackUpAsync(cut);

        Assert.Equal("Last backup: Friday, October 23", LastBackupField(cut));
    }

    // UI-11: what the read throws goes to ErrorHub even while the bar is open, as a lookup's does, so the shell
    // shows the error dialog and carries on. The bar's ErrorBoundary would close it, and leave it closed, over one
    // read.
    [Fact]
    public async Task BackedUp_ReadFails_ReportsItToErrorHub()
    {
        var cut = Render<StatusBar>();
        _lastBackup.Failure = new InvalidOperationException("The read failed.");

        await BackUpAsync(cut);

        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.True(_crashed);
    }

    // The field keeps the backup it showed, rather than saying there's none.
    [Fact]
    public async Task BackedUp_ReadFails_KeepsTheBackupItShowed()
    {
        _lastBackup.MadeAt = FridayNight;
        var cut = Render<StatusBar>();
        _lastBackup.Failure = new InvalidOperationException("The read failed.");

        await BackUpAsync(cut);

        Assert.Equal("Last backup: Friday, October 23", LastBackupField(cut));
    }

    // The read as the bar opens goes to ErrorHub too, and the bar still opens, saying no backup yet, as it does
    // when Core can't read the backups folder.
    [Fact]
    public void Render_ReadFails_ReportsItAndStillOpens()
    {
        _lastBackup.Failure = new InvalidOperationException("The read failed.");

        var cut = Render<StatusBar>();

        Assert.True(_crashed);
        Assert.Equal("No backup yet", LastBackupField(cut));
    }

    // The bar listens before it reads, so a backup made while it reads, as Close Out's can be while a new shell
    // opens, shows once the bar hears of it. The news comes from Close Out's thread while the bar is still opening.
    [Fact]
    public void Render_BackupMadeWhileTheBarReads_ShowsIt()
    {
        _lastBackup.MadeWhileReading = FridayNight;

        var cut = Render<StatusBar>();

        Assert.Null(_lastBackup.ListenerFailure);
        cut.WaitForAssertion(() => Assert.Equal("Last backup: Friday, October 23", LastBackupField(cut)));
    }

    // Leaving stops the listening, as it does for scans.
    [Fact]
    public async Task Dispose_BarOpen_StopsListeningForBackups()
    {
        _ = Render<StatusBar>();

        await DisposeComponentsAsync();

        Assert.False(_lastBackup.IsListenedTo);
    }

    // A backup made just before the status bar closed is heard of afterwards. There's no bar left to show it, so
    // the folder isn't read again, and nothing is raised, though this read would throw.
    [Fact]
    public async Task Dispose_BackupMadeJustBefore_DoesNothing()
    {
        _ = Render<StatusBar>();
        var heardJustBefore = _lastBackup.RaisedBeforeStopping();
        await DisposeComponentsAsync();
        _lastBackup.Failure = new InvalidOperationException("The read failed.");

        await Task.Run(heardJustBefore, Xunit.TestContext.Current.CancellationToken);
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind anything the news started

        Assert.Equal(1, _lastBackup.Reads); // the one as the bar opened
        Assert.False(_crashed);
    }

    // A scan, as the scanner's thread raises it, then a turn of the bar's own thread, so the bar has
    // handled it before the test looks.
    private async Task ScanAsync(IRenderedComponent<StatusBar> cut, string code, ScanKind kind = ScanKind.MemberCard)
    {
        _scanner.Emit(new Scan(kind, code));
        await cut.InvokeAsync(() => { });
    }

    // News of a backup, as the thread that made it raises it, then a turn of the bar's own thread, so the bar
    // has read again before the test looks.
    private async Task BackUpAsync(IRenderedComponent<StatusBar> cut)
    {
        await Task.Run(_lastBackup.BackUp, Xunit.TestContext.Current.CancellationToken);
        await cut.InvokeAsync(() => { });
    }

    private static string LastScan(IRenderedComponent<StatusBar> cut) => cut.Find(".status-bar-field").TextContent;

    // The third field, after the last scan's and the scanner's.
    private static string LastBackupField(IRenderedComponent<StatusBar> cut) => cut.FindAll(".status-bar-field")[2].TextContent;

    private static List<string> Fields(IRenderedComponent<StatusBar> cut) =>
        [.. cut.FindAll(".status-bar-field").Select(field => field.TextContent)];
}
