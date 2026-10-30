using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Rewind.Core.Concessions;
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

    private readonly FakeScanner _scanner = new();
    private readonly FakeMemberRoster _roster = new();
    private readonly FakeConcessionBar _bar = new();
    private readonly ListLogger<ErrorHub> _errorLog = new();
    private bool _crashed;

    public StatusBarTests()
    {
        var errors = new ErrorHub(_errorLog);
        errors.Crashed += () => _crashed = true;
        Services.AddSingleton<IScanner>(_scanner);
        Services.AddSingleton<IMemberRoster>(_roster);
        Services.AddSingleton<IConcessionBar>(_bar);
        Services.AddSingleton(errors);
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
    // and carries on. App's ErrorBoundary, the bar's only one, would replace the whole shell over a name.
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

    // The status bar closes only when the shell crashes or Rewind closes, and that stops a lookup still
    // waiting on the database.
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

    // A scan, as the scanner's thread raises it, then a turn of the bar's own thread, so the bar has
    // handled it before the test looks.
    private async Task ScanAsync(IRenderedComponent<StatusBar> cut, string code, ScanKind kind = ScanKind.MemberCard)
    {
        _scanner.Emit(new Scan(kind, code));
        await cut.InvokeAsync(() => { });
    }

    private static string LastScan(IRenderedComponent<StatusBar> cut) => cut.Find(".status-bar-field").TextContent;
}
