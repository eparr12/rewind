using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Rewind.Core.Devices;
using Rewind.Core.Members;
using Rewind.Core.Scans;
using Rewind.Core.Sounds;
using Rewind.UI.Dialogs;
using Rewind.UI.Errors;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class MembersTabTests : BunitContext
{
    private static readonly MemberRow Grandma = new(1, "Grandma", "MEM-001", IsActive: true);
    private static readonly MemberRow Dad = new(2, "Dad", "MEM-002", IsActive: false);

    private readonly FakeMemberRoster _roster = new();
    private readonly FakeScanner _scanner = new();
    private readonly FakeSoundPlayer _sounds = new();
    private readonly DialogService _dialogs;
    private readonly ListLogger<ErrorHub> _errorLog = new();
    private readonly ErrorHub _errors;
    private bool _crashed;

    public MembersTabTests()
    {
        _errors = new ErrorHub(_errorLog);
        _errors.Crashed += () => _crashed = true;
        Services.AddSingleton(_errors);
        _dialogs = new DialogService(_sounds);
        Services.AddSingleton<IMemberRoster>(_roster);
        Services.AddSingleton<IScanner>(_scanner);
        Services.AddSingleton<ISoundPlayer>(_sounds);
        Services.AddSingleton(_dialogs);
    }

    [Fact]
    public void Render_Members_ListsEachWithTheirCard()
    {
        _roster.Rows.AddRange([Grandma, Dad]);

        var cut = Render<MembersTab>();

        Assert.Equal([["Grandma", "MEM-001", "Yes"], ["Dad", "MEM-002", "No"]], Rows(cut));
    }

    // UI-6: an empty list says so, rather than showing an empty table.
    [Fact]
    public void Render_NoMembers_SaysSo()
    {
        var cut = Render<MembersTab>();

        Assert.Contains(cut.FindAll("p"), p => p.TextContent == "No members yet.");
    }

    [Fact]
    public void Render_WhileReading_SaysSo()
    {
        _roster.Listing = new TaskCompletionSource<IReadOnlyList<MemberRow>>().Task;

        var cut = Render<MembersTab>();

        Assert.Contains(cut.FindAll("p"), p => p.TextContent == "Reading the members...");
    }

    // UI-3: the Store Manager can type the next member's name as soon as the tab opens.
    [Fact]
    public void Render_TabOpens_FocusesTheNameBox()
    {
        _ = Render<MembersTab>();

        JSInterop.VerifyFocusAsyncInvoke();
    }

    // A member added here goes at the end, where the Store Manager sees them, rather than among the others.
    [Fact]
    public async Task Scan_MemberCardAfterAName_AddsThemAtTheEnd()
    {
        _roster.Rows.Add(Grandma);
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");

        await ScanAsync(cut, "MEM-003");

        Assert.Equal(["Add Mom MEM-003"], _roster.Calls);
        Assert.Equal(["Grandma", "Mom"], Rows(cut).Select(row => row[0]));
    }

    [Fact]
    public async Task Scan_MemberCardAfterAName_SaysWhoWasAdded()
    {
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");

        await ScanAsync(cut, "MEM-003");

        Assert.Equal("Added Mom with card MEM-003.", cut.Find(".members-prompt").TextContent);
    }

    // So the next member's name can be typed straight away.
    [Fact]
    public async Task Scan_MemberCardAfterAName_EmptiesTheNameBox()
    {
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");

        await ScanAsync(cut, "MEM-003");

        Assert.Equal("", cut.Find("#members-name").GetAttribute("value"));
    }

    // UI-7: the register chime, as when a case goes on the shelf, so the scan is heard to have taken.
    [Fact]
    public async Task Scan_MemberCardAfterAName_Chimes()
    {
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");

        await ScanAsync(cut, "MEM-003");

        Assert.Equal([Sound.Chime], _sounds.Played);
    }

    // A scan isn't a UI event, so the tab draws the box disabled itself while Core saves.
    [Fact]
    public async Task Scan_WhileSaving_DisablesTheNameBox()
    {
        _roster.Adding = new TaskCompletionSource<MemberChange>().Task;
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");

        await ScanAsync(cut, "MEM-003");

        Assert.True(cut.Find("#members-name").HasAttribute("disabled"));
    }

    // Core decides what counts as a name, so the tab asks it, and says what Core answered: a refusal,
    // worded unlike the prompt, so it's seen to be one.
    [Fact]
    public async Task Scan_MemberCardWithNoName_SaysThereIsNoNameYet()
    {
        _roster.Adding = Task.FromResult(new MemberChange(MemberChangeStatus.NoName, null));
        var cut = Render<MembersTab>();

        await ScanAsync(cut, "MEM-003");

        Assert.Equal("No name yet. Type it, then scan the card again.", cut.Find(".members-prompt").TextContent);
    }

    [Fact]
    public async Task Scan_CardInUse_ShowsTheErrorDialog()
    {
        _roster.Adding = Task.FromResult(new MemberChange(MemberChangeStatus.CardInUse, Grandma));
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");

        await ScanAsync(cut, "MEM-001");

        Assert.Equal(DialogIcon.Error, _dialogs.Current?.Icon);
        Assert.Equal("MEM-001 is Grandma's card already.\n\nScan a card nobody has yet.", _dialogs.Current?.Message);
    }

    // A card stays with its member for good (DATA-11), so a deactivated member is reactivated, not added again.
    [Fact]
    public async Task Scan_CardOfADeactivatedMember_SaysToReactivateThem()
    {
        _roster.Adding = Task.FromResult(new MemberChange(MemberChangeStatus.CardInUse, Dad));
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Dad");

        await ScanAsync(cut, "MEM-002");

        Assert.Equal(
            "MEM-002 is Dad's card already, and Dad is deactivated.\n\nReactivate Dad instead, or scan a card nobody has yet.",
            _dialogs.Current?.Message);
    }

    // HW-5: only a member card means anything here. The status bar says what anything else was.
    [Theory]
    [InlineData(ScanKind.StoreLabel, "RV-00042")]
    [InlineData(ScanKind.Upc, "036000291452")]
    public async Task Scan_NotAMemberCard_IsIgnored(ScanKind kind, string code)
    {
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");

        await ScanAsync(cut, code, kind);

        Assert.Empty(_roster.Calls);
    }

    // One thing at a time: a card scanned while the list is read has no list to join yet.
    [Fact]
    public async Task Scan_WhileTheListIsRead_IsIgnored()
    {
        _roster.Listing = new TaskCompletionSource<IReadOnlyList<MemberRow>>().Task;
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");

        await ScanAsync(cut, "MEM-003");

        Assert.Empty(_roster.Calls);
    }

    // A second card scanned while the first is saved would add the same name twice.
    [Fact]
    public async Task Scan_WhileSaving_IsIgnored()
    {
        _roster.Adding = new TaskCompletionSource<MemberChange>().Task;
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");
        await ScanAsync(cut, "MEM-003");

        await ScanAsync(cut, "MEM-004");

        Assert.Equal(["Add Mom MEM-003"], _roster.Calls);
    }

    // A card scanned while the card-in-use dialog waits would start a new add behind it.
    [Fact]
    public async Task Scan_WhileADialogWaits_IsIgnored()
    {
        _roster.Adding = Task.FromResult(new MemberChange(MemberChangeStatus.CardInUse, Grandma));
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");
        await ScanAsync(cut, "MEM-001");

        await ScanAsync(cut, "MEM-003");

        Assert.Equal(["Add Mom MEM-001"], _roster.Calls);
    }

    // One thing at a time: a card scanned while a name is fixed would add someone mid-rename.
    [Fact]
    public async Task Scan_WhileANameIsFixed_IsIgnored()
    {
        _roster.Rows.Add(Grandma);
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());

        await ScanAsync(cut, "MEM-003");

        Assert.Empty(_roster.Calls);
    }

    [Fact]
    public async Task Rename_Clicked_PutsTheNameInABox()
    {
        _roster.Rows.Add(Grandma);
        var cut = Render<MembersTab>();

        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());

        Assert.Equal("Grandma", cut.Find(".members-rename").GetAttribute("value"));
    }

    // UI-3: Enter saves the name.
    [Fact]
    public async Task Rename_EnterPressed_SavesTheNewName()
    {
        _roster.Rows.Add(Grandma);
        var cut = Render<MembersTab>();
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());
        await cut.Find(".members-rename").InputAsync("Grandma Rose");

        await cut.Find(".members-rename").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(["Rename 1 Grandma Rose"], _roster.Calls);
        Assert.Equal([["Grandma Rose", "MEM-001", "Yes"]], Rows(cut));
    }

    // UI-3: Esc cancels, and the name stays as it was.
    [Fact]
    public async Task Rename_EscPressed_KeepsTheName()
    {
        _roster.Rows.Add(Grandma);
        var cut = Render<MembersTab>();
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());
        await cut.Find(".members-rename").InputAsync("Grandma Rose");

        await cut.Find(".members-rename").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(_roster.Calls);
        Assert.Equal([["Grandma", "MEM-001", "Yes"]], Rows(cut));
    }

    // The box stays open, so the name can be typed again.
    [Fact]
    public async Task Rename_BlankName_SaysAMemberNeedsOne()
    {
        _roster.Rows.Add(Grandma);
        _roster.Renaming = Task.FromResult(new MemberChange(MemberChangeStatus.NoName, null));
        var cut = Render<MembersTab>();
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());
        await cut.Find(".members-rename").InputAsync(" ");

        await Button(cut, "Save").ClickAsync(new MouseEventArgs());

        Assert.Equal("A member needs a name.", cut.Find(".members-prompt").TextContent);
        Assert.Single(cut.FindAll(".members-rename"));
    }

    // DATA-4: deactivated, the member stays in the list, with a button to bring them back.
    [Fact]
    public async Task Deactivate_Clicked_ShowsThemInactive()
    {
        _roster.Rows.Add(Grandma);
        var cut = Render<MembersTab>();

        await Button(cut, "Deactivate").ClickAsync(new MouseEventArgs());

        Assert.Equal(["SetActive 1 False"], _roster.Calls);
        Assert.Equal([["Grandma", "MEM-001", "No"]], Rows(cut));
        Assert.Single(cut.FindAll("button"), button => button.TextContent == "Reactivate");
    }

    [Fact]
    public async Task Reactivate_Clicked_ShowsThemActive()
    {
        _roster.Rows.Add(Dad);
        var cut = Render<MembersTab>();

        await Button(cut, "Reactivate").ClickAsync(new MouseEventArgs());

        Assert.Equal(["SetActive 2 True"], _roster.Calls);
        Assert.Equal([["Dad", "MEM-002", "Yes"]], Rows(cut));
    }

    // Leaving the tab stops a save still waiting on the database.
    [Fact]
    public async Task Dispose_WhileAdding_CancelsTheSave()
    {
        _roster.Adding = new TaskCompletionSource<MemberChange>().Task;
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");
        await ScanAsync(cut, "MEM-003");

        await DisposeComponentsAsync();

        Assert.True(_roster.LastToken.IsCancellationRequested);
    }

    // A save can finish just as the clerk leaves, before the cancellation reaches it. Its answer is
    // dropped, as in F5's print flow, so a card in use opens no dialog over whatever screen is showing.
    [Fact]
    public async Task Dispose_WhileAddingACardInUse_OpensNoDialog()
    {
        var adding = new TaskCompletionSource<MemberChange>();
        _roster.Adding = adding.Task;
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");
        await ScanAsync(cut, "MEM-001");
        await DisposeComponentsAsync();

        adding.SetResult(new MemberChange(MemberChangeStatus.CardInUse, Grandma));
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the tab's own continuation

        Assert.Null(_dialogs.Current);
    }

    // Leaving mid-save shows nothing. A scan's save runs as a fire-and-forget task, which ComponentBase
    // never sees, so the tab catches its own cancellation. Without the catch, the cancellation would go
    // to ErrorHub as an error from a closed screen, and the clerk would see the error dialog.
    [Fact]
    public async Task Dispose_WhileAdding_RaisesNothingOnceCancelled()
    {
        var adding = new TaskCompletionSource<MemberChange>();
        _roster.Adding = adding.Task;
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");
        await ScanAsync(cut, "MEM-003");
        await DisposeComponentsAsync();

        adding.SetCanceled(_roster.LastToken); // as EF Core ends a save whose token was cancelled
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the tab's own continuation

        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
    }

    // #35: a save that fails after the clerk left has no tab to show it on, so it goes to ErrorHub,
    // which logs it, naming the tab (OBS-3), and shows the error dialog (OBS-5).
    [Fact]
    public async Task Dispose_WhileAdding_ReportsAFailureToErrorHub()
    {
        var adding = new TaskCompletionSource<MemberChange>();
        _roster.Adding = adding.Task;
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");
        await ScanAsync(cut, "MEM-003");
        await DisposeComponentsAsync();

        adding.SetException(new InvalidOperationException("The save failed."));
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the tab's own continuation

        Assert.True(_crashed);
        Assert.Equal(["Unhandled exception from MembersTab; reported to the shell"], _errorLog.Entries.Select(entry => entry.Message));
    }

    // While the tab is open, a failure goes to its ErrorBoundary, as before, not to ErrorHub.
    [Fact]
    public async Task Scan_SaveFails_HandsItToTheErrorBoundary()
    {
        _roster.Adding = Task.FromException<MemberChange>(new InvalidOperationException("The save failed."));
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");

        await ScanAsync(cut, "MEM-003");

        Assert.True(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
    }

    // A failure is handed on in the same turn of the renderer's thread that it's thrown in, so it
    // reaches the ErrorBoundary even when the clerk leaves that turn, before the tab is disposed.
    [Fact]
    public async Task Scan_SaveFailsInTheTurnTheClerkLeaves_ReachesTheErrorBoundary()
    {
        _roster.Adding = Task.FromException<MemberChange>(new InvalidOperationException("The save failed."));
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");
        var leaving = Task.CompletedTask;

        var handedOn = await cut.InvokeAsync(() =>
        {
            _scanner.Emit(new Scan(ScanKind.MemberCard, "MEM-003"));
            var reached = Renderer.UnhandledException.IsCompleted; // read now: disposing starts a new record
            leaving = DisposeComponentsAsync();
            return reached;
        });
        await leaving;

        Assert.True(handedOn);
        Assert.False(_crashed);
    }

    // A tab that kept listening after it closed would add members from another tab's scans.
    [Fact]
    public async Task Dispose_Any_StopsListeningForScans()
    {
        _ = Render<MembersTab>();

        await DisposeComponentsAsync();

        Assert.False(_scanner.IsListenedTo);
    }

    // A scan the scanner's thread raised just before the clerk left reaches the tab afterwards, with a
    // name typed. Its token went with it, so nobody is added, and nothing is raised. Reading that token
    // would throw, and the clerk would see the error dialog wherever they went.
    [Fact]
    public async Task Dispose_ScanRaisedJustBefore_RaisesNothing()
    {
        var cut = Render<MembersTab>();
        await cut.Find("#members-name").InputAsync("Mom");
        var raisedJustBefore = _scanner.RaisedBeforeStopping();
        await DisposeComponentsAsync();

        raisedJustBefore(new Scan(ScanKind.MemberCard, "MEM-003"));
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind anything the scan started

        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
    }

    // A scan, as the scanner's thread raises it, then a turn of the tab's own thread, so the tab has
    // handled it before the test looks.
    private async Task ScanAsync(IRenderedComponent<MembersTab> cut, string code, ScanKind kind = ScanKind.MemberCard)
    {
        _scanner.Emit(new Scan(kind, code));
        await cut.InvokeAsync(() => { });
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<MembersTab> cut, string text) =>
        cut.FindAll("button").Single(button => button.TextContent == text);

    // Each member's name, card and whether they're active, as the list shows them.
    private static List<string[]> Rows(IRenderedComponent<MembersTab> cut) =>
        [.. cut.FindAll(".members-list tbody tr").Select(row => row.QuerySelectorAll("td").Take(3).Select(cell => cell.TextContent).ToArray())];
}
