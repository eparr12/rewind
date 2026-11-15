using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Rewind.Core.Devices;
using Rewind.Core.Members;
using Rewind.Core.Returns;
using Rewind.Core.Scans;
using Rewind.Core.Sounds;
using Rewind.UI.Dialogs;
using Rewind.UI.Errors;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class ReturnTests : BunitContext
{
    private const string Label = "RV-00042";
    private const string OtherLabel = "RV-00043";
    private const string FactoryUpc = "032429258373";
    private const string Idle = "Scan a returned case to put it back on the shelf.";
    private const string Asking = "Pick who watched and their stars, then press Enter to save, or Esc to skip.";
    private const string Back = "RV-00042 is back on the shelf.";
    private const string Skipped = "RV-00042 is back on the shelf. Scanning it again asks who watched.";

    // The roster's order runs against both name and id, as in WhoWatchedTests, so a line that sorted the
    // watchers would show it.
    private static readonly MemberRow Mom = new(12, "Mom", "MEM-003", IsActive: true);
    private static readonly MemberRow Dad = new(7, "Dad", "MEM-002", IsActive: true);
    private static readonly MemberRow Sam = new(3, "Sam", "MEM-001", IsActive: true);

    // 6:30 PM on Friday, October 23, 2026, on a register clock four hours behind UTC (TEST-4).
    private static readonly DateTime FridayEvening = new(2026, 10, 23, 22, 30, 0, DateTimeKind.Utc);

    private static readonly ReturnedRental Halloween = new(5, Label, "Halloween", 1978, "Dad", FridayEvening, [Mom, Dad, Sam]);
    private static readonly ReturnedRental Scream = new(9, OtherLabel, "Scream", 1996, "Mom", FridayEvening, [Mom, Dad, Sam]);

    private readonly FakeReturnDesk _desk = new();
    private readonly FakeScanner _scanner = new();
    private readonly FakeSoundPlayer _sounds = new();
    // 1:15 AM on Saturday, on that clock: Friday's night has run past midnight, and it's still Friday's.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 24, 5, 15, 0, TimeSpan.Zero));
    private readonly DialogService _dialogs;
    private readonly ListLogger<Return> _log = new();
    private readonly ListLogger<ErrorHub> _errorLog = new();
    private bool _crashed;

    public ReturnTests()
    {
        var errors = new ErrorHub(_errorLog);
        errors.Crashed += () => _crashed = true;
        _dialogs = new DialogService(_sounds);
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Register", TimeSpan.FromHours(-4), "Register", "Register"));
        _desk.Rentals[Label] = Halloween;
        _desk.Rentals[OtherLabel] = Scream;
        Services.AddSingleton<IReturnDesk>(_desk);
        Services.AddSingleton<IScanner>(_scanner);
        Services.AddSingleton<ISoundPlayer>(_sounds);
        Services.AddSingleton<TimeProvider>(_time);
        Services.AddSingleton(_dialogs);
        Services.AddSingleton<ILogger<Return>>(_log);
        Services.AddSingleton(errors);
    }

    // UI-6: the placeholder's words, until a case is scanned.
    [Fact]
    public void Render_ScreenOpens_SaysToScanAReturnedCase()
    {
        var cut = Render<Return>();

        Assert.Equal(Idle, Prompt(cut));
        Assert.Null(Last(cut));
        Assert.Empty(cut.FindAll(".who-watched"));
    }

    // HW-5: ScannerHub logged it and played the chord, so the screen has nothing to add.
    [Fact]
    public async Task Scanned_UnknownBarcode_DoesNothing()
    {
        var cut = Render<Return>();

        await ScanAsync(cut, ScanKind.Unknown, "WHAT-IS-THIS");

        Assert.Null(_dialogs.Current);
        Assert.Empty(_log.Entries);
        Assert.Empty(_desk.Labels);
    }

    // The Overview's words, as at Checkout.
    [Fact]
    public async Task Scanned_FactoryUpc_AsksForTheLabel()
    {
        var cut = Render<Return>();

        await ScanAsync(cut, ScanKind.Upc, FactoryUpc);

        AssertWarning("Please scan the Rewind Video label.");
        Assert.Empty(_desk.Labels);
    }

    [Theory]
    [InlineData(ScanKind.MemberCard, "MEM-001")]
    [InlineData(ScanKind.Concession, "CON-POPCORN")]
    public async Task Scanned_CardOrSnack_SaysReturnTakesAMoviesLabel(ScanKind kind, string code)
    {
        var cut = Render<Return>();

        await ScanAsync(cut, kind, code);

        AssertWarning("Return takes a movie's store label.");
        Assert.Empty(_desk.Labels);
    }

    [Fact]
    public async Task Scanned_Label_ChecksItInWithABusyLine()
    {
        _desk.CheckingIn = new TaskCompletionSource<CheckIn>().Task;
        var cut = Render<Return>();

        await ScanLabelAsync(cut);

        Assert.Equal("Checking in RV-00042...", Prompt(cut));
        Assert.Equal([Label], _desk.Labels);
    }

    // Build Plan 3.4: the scan puts the case back on the shelf, and then Rewind asks who watched.
    [Fact]
    public async Task Scanned_CaseCheckedIn_ChimesSaysItsBackAndAsksWhoWatched()
    {
        var cut = Render<Return>();

        await ScanLabelAsync(cut);

        Assert.Equal([Sound.Chime], _sounds.Played);
        Assert.Equal(Back, Last(cut));
        Assert.Equal(["Mom:", "Dad:", "Sam:"], cut.FindAll(".who-watched label").Select(label => label.TextContent));
        Assert.Equal(Asking, Prompt(cut));
    }

    // The chime says the case is back, so it waits for the save.
    [Fact]
    public async Task Scanned_CheckInStillGoing_DoesNotChimeYet()
    {
        var checkIn = new TaskCompletionSource<CheckIn>();
        _desk.CheckingIn = checkIn.Task;
        var cut = Render<Return>();

        await ScanLabelAsync(cut);

        Assert.Empty(_sounds.Played);
        checkIn.SetResult(new CheckIn(CheckInStatus.CheckedIn, Halloween));
        await cut.InvokeAsync(() => { }); // queued behind the screen's own continuation
        Assert.Equal([Sound.Chime], _sounds.Played);
    }

    // The line above the form: the case, its movie, who rented it, and the night.
    [Fact]
    public async Task Scanned_CaseCheckedIn_SaysWhatsBeingAsked()
    {
        var cut = Render<Return>();

        await ScanLabelAsync(cut);

        Assert.Equal("RV-00042 · Halloween (1978) · Rented by Dad on Friday night", AskingLine(cut));
    }

    // DATA-8: a night that opened at 9:30 PM on Friday had passed midnight in UTC, and it's still Friday's.
    [Fact]
    public async Task Scanned_NightOpenedLateOnFriday_NamesItFridayInTheRegistersTime()
    {
        _desk.Rentals[Label] = Halloween with { NightOpenedAt = new DateTime(2026, 10, 24, 1, 30, 0, DateTimeKind.Utc) };
        var cut = Render<Return>();

        await ScanLabelAsync(cut);

        Assert.Equal("RV-00042 · Halloween (1978) · Rented by Dad on Friday night", AskingLine(cut));
    }

    // DATA-3: a movie whose year isn't known goes by its title alone.
    [Fact]
    public async Task Scanned_MovieWithNoYear_NamesItByItsTitle()
    {
        _desk.Rentals[Label] = Halloween with { Year = null };
        var cut = Render<Return>();

        await ScanLabelAsync(cut);

        Assert.Equal("RV-00042 · Halloween · Rented by Dad on Friday night", AskingLine(cut));
    }

    // Everyone deactivated while the case was out leaves nobody to ask, so there's no form.
    [Fact]
    public async Task Scanned_CaseCheckedInWithNobodyToAsk_SaysItsBackWithoutAForm()
    {
        _desk.Rentals[Label] = Halloween with { People = [] };
        var cut = Render<Return>();

        await ScanLabelAsync(cut);

        Assert.Equal([Sound.Chime], _sounds.Played);
        Assert.Equal(Back, Last(cut));
        Assert.Empty(cut.FindAll(".who-watched"));
        Assert.Equal(Idle, Prompt(cut));
    }

    // ADR-0012: its last rental's watch log is empty, so Rewind asks. Nothing was saved now, so nothing chimes.
    [Fact]
    public async Task Scanned_CaseAlreadyBackWithARentalToAsk_AsksWithoutAChime()
    {
        _desk.CheckingIn = Task.FromResult(new CheckIn(CheckInStatus.AlreadyOnShelf, Halloween));
        var cut = Render<Return>();

        await ScanLabelAsync(cut);

        Assert.Empty(_sounds.Played);
        Assert.Equal("RV-00042 was already back on the shelf.", Last(cut));
        Assert.Equal("RV-00042 · Halloween (1978) · Rented by Dad on Friday night", AskingLine(cut));
        Assert.Equal(Asking, Prompt(cut));
    }

    [Fact]
    public async Task Scanned_CaseAlreadyBackWithNothingToAsk_SaysSo()
    {
        _desk.CheckingIn = Task.FromResult(new CheckIn(CheckInStatus.AlreadyOnShelf, null));
        var cut = Render<Return>();

        await ScanLabelAsync(cut);

        AssertWarning("RV-00042 is already on the shelf.");
        Assert.Empty(cut.FindAll(".who-watched"));
    }

    // The form opens only with someone to ask, so a rental whose members have all been deactivated has nothing.
    [Fact]
    public async Task Scanned_CaseAlreadyBackWithNobodyToAsk_SaysSo()
    {
        _desk.CheckingIn = Task.FromResult(new CheckIn(CheckInStatus.AlreadyOnShelf, Halloween with { People = [] }));
        var cut = Render<Return>();

        await ScanLabelAsync(cut);

        AssertWarning("RV-00042 is already on the shelf.");
        Assert.Empty(cut.FindAll(".who-watched"));
    }

    // Checkout's words.
    [Fact]
    public async Task Scanned_LabelNoCaseHas_SaysSo()
    {
        var cut = Render<Return>();

        await ScanLabelAsync(cut, "RV-99999");

        AssertWarning("No case has the label RV-99999.\n\nTell the Store Manager.");
    }

    [Fact]
    public async Task Scanned_RetiredCase_SaysSo()
    {
        _desk.CheckingIn = Task.FromResult(new CheckIn(CheckInStatus.Retired, null));
        var cut = Render<Return>();

        await ScanLabelAsync(cut);

        AssertWarning("RV-00042 has been retired, so it can't be checked in.\n\nTell the Store Manager.");
    }

    // A refused case saved nothing, so the dialog's chord is all that plays, and its dialog says what happened.
    [Theory]
    [InlineData(CheckInStatus.AlreadyOnShelf)]
    [InlineData(CheckInStatus.NoSuchCase)]
    [InlineData(CheckInStatus.Retired)]
    public async Task Scanned_CaseRefused_PlaysNoChimeAndOpensNoForm(CheckInStatus status)
    {
        _desk.CheckingIn = Task.FromResult(new CheckIn(status, null));
        var cut = Render<Return>();

        await ScanLabelAsync(cut);

        Assert.Equal([Sound.Error], _sounds.Played);
        Assert.Null(Last(cut));
        Assert.Empty(cut.FindAll(".who-watched"));
    }

    // A hand edit left it checked out with no rental out. It's back and saved, so it chimes, and with no rental
    // there's nobody to ask, which the screen's line says rather than a dialog.
    [Fact]
    public async Task Scanned_CaseWithNoRentalOut_ChimesAndSaysThereIsNobodyToAsk()
    {
        _desk.CheckingIn = Task.FromResult(new CheckIn(CheckInStatus.Reshelved, null));
        var cut = Render<Return>();

        await ScanLabelAsync(cut);

        Assert.Equal([Sound.Chime], _sounds.Played);
        Assert.Equal("RV-00042 is back on the shelf. Rewind had no rental for it, so there's nobody to ask.", Last(cut));
        Assert.Null(_dialogs.Current);
        Assert.Empty(cut.FindAll(".who-watched"));
    }

    // One thing at a time, as at Checkout.
    [Fact]
    public async Task Scanned_WhileCoreIsAnswering_IsIgnoredAndLogged()
    {
        _desk.CheckingIn = new TaskCompletionSource<CheckIn>().Task;
        var cut = Render<Return>();
        await ScanLabelAsync(cut);

        await ScanLabelAsync(cut, OtherLabel);

        Assert.Equal([Label], _desk.Labels);
        AssertLoggedIgnored("Ignored the StoreLabel RV-00043: Return is still busy or a dialog is open");
    }

    [Fact]
    public async Task Scanned_WhileADialogIsOpen_IsIgnoredAndLogged()
    {
        var cut = Render<Return>();
        await ScanAsync(cut, ScanKind.Upc, FactoryUpc);

        await ScanLabelAsync(cut);

        Assert.Empty(_desk.Labels);
        AssertLoggedIgnored("Ignored the StoreLabel RV-00042: Return is still busy or a dialog is open");
    }

    // While who watched is being saved, a label is ignored, rather than told to finish a form that's closing.
    [Fact]
    public async Task Scanned_WhileSaving_IsIgnoredAndLogged()
    {
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Dad, "5 stars");
        _desk.Logging = new TaskCompletionSource<WatchLogStatus>().Task;
        _ = PressAsync(cut, "Save");

        await ScanLabelAsync(cut, OtherLabel);

        Assert.Null(_dialogs.Current);
        AssertLoggedIgnored("Ignored the StoreLabel RV-00043: Return is still busy or a dialog is open");
    }

    // One return at a time.
    [Fact]
    public async Task Scanned_LabelWhileTheFormIsOpen_SaysToFinishThisOneFirst()
    {
        var cut = await RenderAskingAsync();

        await ScanLabelAsync(cut, OtherLabel);

        AssertWarning("Say who watched Halloween (1978) first, or press Esc to skip it.\n\nThen scan RV-00043 again.");
    }

    // A second read of the label is the case being asked about, so there's nothing to scan again.
    [Fact]
    public async Task Scanned_SameLabelWhileItsFormIsOpen_SaysItsCheckedInAlready()
    {
        var cut = await RenderAskingAsync();

        await ScanLabelAsync(cut);

        AssertWarning("RV-00042 is checked in already. Say who watched Halloween (1978), or press Esc to skip it.");
    }

    // Read out and closed, the warning leaves the form asking about the first case, and the label it was for
    // checked nothing in: that case is still out until it's scanned again.
    [Theory]
    [InlineData(OtherLabel)]
    [InlineData(Label)]
    public async Task CloseDialog_LabelWhileTheFormIsOpen_ChecksNothingInAndKeepsAsking(string label)
    {
        var cut = await RenderAskingAsync();
        await ScanLabelAsync(cut, label);

        await CloseDialogAsync(cut);

        Assert.Equal([Label], _desk.Labels);
        Assert.Equal("RV-00042 · Halloween (1978) · Rented by Dad on Friday night", AskingLine(cut));
        Assert.Equal(Back, Last(cut));
    }

    // A refused scan leaves the form as it was, picks and all.
    [Fact]
    public async Task CloseDialog_CardWhileTheFormIsOpen_KeepsTheFormAndItsPicks()
    {
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Dad, "5 stars");
        await ScanAsync(cut, ScanKind.MemberCard, "MEM-001");

        await CloseDialogAsync(cut);

        Assert.Equal("5 stars", Chosen(ListOf(cut, Dad)));
        Assert.Equal(Back, Last(cut));
        Assert.Equal(Asking, Prompt(cut));
    }

    // Build Plan 3.4's Done when: the returned movie goes in the watch log with each person's stars.
    [Fact]
    public async Task ClickSave_DoneWhenReturn_LogsEachPersonsStarsForThatRental()
    {
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Mom, "4 stars");
        await PickAsync(cut, Dad, "5 stars");
        await PickAsync(cut, Sam, "Watched, no stars");

        await SaveAsync(cut);

        var (rentalItemId, watchers) = Assert.Single(_desk.Logs);
        Assert.Equal(Halloween.RentalItemId, rentalItemId);
        Assert.Equal([new Watcher(Mom.Id, 4), new Watcher(Dad.Id, 5), new Watcher(Sam.Id, null)], watchers);
    }

    // The watchers are named in the order the form sent them, the roster's.
    [Fact]
    public async Task ClickSave_Logged_ChimesClosesTheFormAndSaysWhatWasSaved()
    {
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Mom, "4 stars");
        await PickAsync(cut, Dad, "5 stars");
        await PickAsync(cut, Sam, "Watched, no stars");

        await SaveAsync(cut);

        Assert.Equal([Sound.Chime, Sound.Chime], _sounds.Played);
        Assert.Empty(cut.FindAll(".who-watched"));
        Assert.Equal("Halloween (1978) is in the watch log. Mom: 4 stars. Dad: 5 stars. Sam: watched.", Last(cut));
        Assert.Equal(Idle, Prompt(cut));
    }

    // The screen says the stars in the words the form offered them in (GEN-8). A key picks them, as the clerk
    // does, so the words come from the form itself.
    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("4")]
    [InlineData("5")]
    public async Task ClickSave_EachNumberOfStars_SaysItInTheFormsWords(string key)
    {
        var cut = await RenderAskingAsync();
        await ListOf(cut, Dad).KeyDownAsync(new KeyboardEventArgs { Key = key });
        var offered = Chosen(ListOf(cut, Dad));

        await SaveAsync(cut);

        Assert.Equal($"Halloween (1978) is in the watch log. Dad: {offered}.", Last(cut));
    }

    // Saved, so not before: a chime ahead of a save that failed would say it had gone through.
    [Fact]
    public async Task ClickSave_SaveStillGoing_DoesNotChimeYet()
    {
        var logged = new TaskCompletionSource<WatchLogStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Dad, "5 stars");
        _desk.Logging = logged.Task;

        var save = PressAsync(cut, "Save");

        Assert.Equal("Saving who watched...", Prompt(cut));
        Assert.Equal([Sound.Chime], _sounds.Played);
        logged.SetResult(WatchLogStatus.Logged);
        await save;
        Assert.Equal([Sound.Chime, Sound.Chime], _sounds.Played);
    }

    // The form has each Enter in a list submit again, and a rental's watch log is saved once (ADR-0012).
    [Fact]
    public async Task Submit_TwiceWhileSaving_SendsOneAnswer()
    {
        var logged = new TaskCompletionSource<WatchLogStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Dad, "5 stars");
        _desk.Logging = logged.Task;
        var first = cut.Find(".who-watched-form").SubmitAsync();

        var second = cut.Find(".who-watched-form").SubmitAsync();

        logged.SetResult(WatchLogStatus.Logged);
        await first;
        await second;
        Assert.Single(_desk.Logs);
    }

    // Esc while Core saves is too late to skip: the answer being saved is the one that counts, so the screen
    // never calls it skipped.
    [Fact]
    public async Task KeyDownEscape_WhileSaving_SkipsNothing()
    {
        var logged = new TaskCompletionSource<WatchLogStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Dad, "5 stars");
        _desk.Logging = logged.Task;
        var save = PressAsync(cut, "Save");

        await Lists(cut)[0].KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(Back, Last(cut));
        Assert.Single(cut.FindAll(".who-watched"));
        logged.SetResult(WatchLogStatus.Logged);
        await save;
    }

    // ADR-0012: a watch log is saved once, so there's nothing left to ask, and nothing chimes for it.
    [Fact]
    public async Task ClickSave_AlreadyLogged_SaysSoAndClosesTheForm()
    {
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Dad, "5 stars");
        _desk.Logging = Task.FromResult(WatchLogStatus.AlreadyLogged);

        var save = PressAsync(cut, "Save");

        AssertWarning("Who watched Halloween (1978) was saved already, so nothing was added.");
        Assert.Empty(cut.FindAll(".who-watched"));
        Assert.Equal([Sound.Chime, Sound.Error], _sounds.Played);
        await CloseDialogAsync(cut);
        await save;
    }

    // The form never sends nobody, but if Core saved nobody, nothing was saved, as with a skip.
    [Fact]
    public async Task ClickSave_CoreSavedNobody_SaysItWasSkipped()
    {
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Dad, "5 stars");
        _desk.Logging = Task.FromResult(WatchLogStatus.NoWatchers);

        await SaveAsync(cut);

        Assert.Equal(Skipped, Last(cut));
        Assert.Empty(cut.FindAll(".who-watched"));
        Assert.Equal([Sound.Chime], _sounds.Played);
    }

    // A save that failed saved nothing, so it mustn't sound as if it had. The screen's ErrorBoundary hears of it.
    [Fact]
    public async Task ClickSave_SaveFails_LeavesItToTheErrorBoundaryWithoutAChime()
    {
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Dad, "5 stars");
        _desk.Logging = Task.FromException<WatchLogStatus>(new InvalidOperationException("The save failed."));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => PressAsync(cut, "Save"));

        Assert.Equal("The save failed.", thrown.Message);
        Assert.Equal([Sound.Chime], _sounds.Played);
        Assert.False(_crashed);
    }

    // ADR-0012: a skip saves nothing, and the case is back already, so its next scan asks again.
    [Fact]
    public async Task ClickSkip_StarsPicked_SavesNothingAndSaysTheCaseIsBack()
    {
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Dad, "5 stars");

        await PressAsync(cut, "Skip");

        Assert.Empty(_desk.Logs);
        Assert.Empty(cut.FindAll(".who-watched"));
        Assert.Equal(Skipped, Last(cut));
        Assert.Equal(Idle, Prompt(cut));
    }

    // The form hears Save with nobody chosen as a skip, so the screen does too, and Core is asked nothing.
    [Fact]
    public async Task ClickSave_NobodyChosen_SavesNothingAndSaysTheCaseIsBack()
    {
        var cut = await RenderAskingAsync();

        await SaveAsync(cut);

        Assert.Empty(_desk.Logs);
        Assert.Equal(Skipped, Last(cut));
        Assert.Equal([Sound.Chime], _sounds.Played);
    }

    [Fact]
    public async Task KeyDownEscape_InTheForm_Skips()
    {
        var cut = await RenderAskingAsync();

        await Lists(cut)[0].KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(_desk.Logs);
        Assert.Empty(cut.FindAll(".who-watched"));
        Assert.Equal(Skipped, Last(cut));
    }

    // ADR-0012: the rental's watch log is still empty, so the rescan asks again, with a new form.
    [Fact]
    public async Task Scanned_AgainAfterASkip_AsksAgainWithANewForm()
    {
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Dad, "5 stars");
        await PressAsync(cut, "Skip");

        await ScanLabelAsync(cut);

        Assert.Equal("RV-00042 was already back on the shelf.", Last(cut));
        Assert.Equal(["Didn't watch", "Didn't watch", "Didn't watch"], Lists(cut).Select(Chosen));
        Assert.Equal([Sound.Chime], _sounds.Played);
    }

    // Each return gets a form of its own, for its own rental, with nobody picked.
    [Fact]
    public async Task Scanned_NextCaseAfterASave_AsksAboutItWithANewForm()
    {
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Dad, "5 stars");
        await SaveAsync(cut);

        await ScanLabelAsync(cut, OtherLabel);

        Assert.Equal("RV-00043 is back on the shelf.", Last(cut));
        Assert.Equal("RV-00043 · Scream (1996) · Rented by Mom on Friday night", AskingLine(cut));
        Assert.Equal(["Didn't watch", "Didn't watch", "Didn't watch"], Lists(cut).Select(Chosen));
    }

    // What happened to the last return stays until the next case is scanned, and a card isn't a case.
    [Fact]
    public async Task CloseDialog_CardAfterAReturn_KeepsWhatHappenedOnScreen()
    {
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Dad, "5 stars");
        await SaveAsync(cut);
        await ScanAsync(cut, ScanKind.MemberCard, "MEM-001");

        await CloseDialogAsync(cut);

        Assert.Equal("Halloween (1978) is in the watch log. Dad: 5 stars.", Last(cut));
    }

    // The next case's own dialog says what happened to it, so the last return's line goes.
    [Fact]
    public async Task Scanned_NextCaseRefused_ClearsWhatHappenedToTheLastOne()
    {
        var cut = await RenderAskingAsync();
        await PressAsync(cut, "Skip");

        await ScanLabelAsync(cut, "RV-99999");

        Assert.Null(Last(cut));
    }

    // The scanner outlives the screen, so the screen stops listening when the clerk leaves it.
    [Fact]
    public async Task Dispose_ClerkLeavesTheScreen_StopsListeningForScans()
    {
        _ = Render<Return>();

        await DisposeComponentsAsync();

        Assert.False(_scanner.IsListenedTo);
    }

    // A scan the scanner's thread raised just before the clerk left reaches the screen afterwards. The screen
    // has closed, so nothing is asked, and nothing is raised.
    [Fact]
    public async Task Dispose_ScanRaisedJustBefore_RaisesNothing()
    {
        _ = Render<Return>();
        var raisedJustBefore = _scanner.RaisedBeforeStopping();
        await DisposeComponentsAsync();

        raisedJustBefore(new Scan(ScanKind.StoreLabel, Label));
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind anything the scan started

        Assert.Empty(_desk.Labels);
        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
    }

    [Fact]
    public async Task Dispose_WhileCheckingIn_CancelsTheCheckIn()
    {
        _desk.CheckingIn = new TaskCompletionSource<CheckIn>().Task;
        var cut = Render<Return>();
        await ScanLabelAsync(cut);

        await DisposeComponentsAsync();

        Assert.True(_desk.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Dispose_WhileSaving_CancelsTheSave()
    {
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Dad, "5 stars");
        _desk.Logging = new TaskCompletionSource<WatchLogStatus>().Task;
        _ = PressAsync(cut, "Save");

        await DisposeComponentsAsync();

        Assert.True(_desk.LastToken.IsCancellationRequested);
    }

    // NET-7: a check-in whose commit beat the clerk leaving is done, and the chime still says so. The screen has
    // gone, so nothing opens: no form, which would have asked for the focus, and no dialog over the next screen.
    [Fact]
    public async Task Dispose_CheckInCommitsAsTheClerkLeaves_StillChimesAndOpensNothing()
    {
        var checkIn = new TaskCompletionSource<CheckIn>();
        _desk.CheckingIn = checkIn.Task;
        var cut = Render<Return>();
        await ScanLabelAsync(cut);
        await DisposeComponentsAsync();

        checkIn.SetResult(new CheckIn(CheckInStatus.CheckedIn, Halloween));
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the screen's own continuation

        Assert.Equal([Sound.Chime], _sounds.Played);
        Assert.Empty(JSInterop.Invocations);
        Assert.Null(_dialogs.Current);
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // ADR-0011: an answer that arrives after the clerk left, rather than being cancelled, would open its dialog
    // over the next screen. The dialogs outlive the screen, so it shows nothing instead.
    [Fact]
    public async Task Dispose_CheckInAnswersAfterTheClerkLeft_ShowsNoDialog()
    {
        var checkIn = new TaskCompletionSource<CheckIn>();
        _desk.CheckingIn = checkIn.Task;
        var cut = Render<Return>();
        await ScanLabelAsync(cut, "RV-99999");
        await DisposeComponentsAsync();

        checkIn.SetResult(new CheckIn(CheckInStatus.NoSuchCase, null));
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the screen's own continuation

        Assert.Null(_dialogs.Current);
        Assert.False(_crashed);
    }

    // It looks before it waits on the save: a dialog would hold the save open, and the test with it.
    [Fact]
    public async Task Dispose_SaveAnswersAfterTheClerkLeft_ShowsNoDialog()
    {
        var logged = new TaskCompletionSource<WatchLogStatus>();
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Dad, "5 stars");
        _desk.Logging = logged.Task;
        var save = PressAsync(cut, "Save");
        await DisposeComponentsAsync();

        logged.SetResult(WatchLogStatus.AlreadyLogged);
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the screen's own continuation

        Assert.Null(_dialogs.Current);
        await save;
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // NET-7: a save whose commit beat the clerk leaving has gone through, and the chime still says so.
    [Fact]
    public async Task Dispose_SaveCommitsAsTheClerkLeaves_StillChimes()
    {
        var logged = new TaskCompletionSource<WatchLogStatus>();
        var cut = await RenderAskingAsync();
        await PickAsync(cut, Dad, "5 stars");
        _desk.Logging = logged.Task;
        var save = PressAsync(cut, "Save");
        await DisposeComponentsAsync();

        logged.SetResult(WatchLogStatus.Logged);
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the screen's own continuation

        Assert.Equal([Sound.Chime, Sound.Chime], _sounds.Played);
        Assert.Null(_dialogs.Current);
        await save;
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // The cancellation leaving causes goes nowhere: WhileBusyAsync catches it, and nothing shows or chimes.
    [Fact]
    public async Task Dispose_WhileCheckingIn_RaisesNothingOnceCancelled()
    {
        var checkIn = new TaskCompletionSource<CheckIn>();
        _desk.CheckingIn = checkIn.Task;
        var cut = Render<Return>();
        await ScanLabelAsync(cut);
        await DisposeComponentsAsync();

        checkIn.SetCanceled(_desk.LastToken); // as EF Core ends a save whose token was cancelled
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the screen's own continuation

        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
        Assert.Null(_dialogs.Current);
        Assert.Empty(_sounds.Played);
    }

    // #35: a check-in that fails after the clerk left has no screen to show it on, so it goes to ErrorHub.
    [Fact]
    public async Task Dispose_WhileCheckingIn_ReportsAFailureToErrorHub()
    {
        var checkIn = new TaskCompletionSource<CheckIn>();
        _desk.CheckingIn = checkIn.Task;
        var cut = Render<Return>();
        await ScanLabelAsync(cut);
        await DisposeComponentsAsync();

        checkIn.SetException(new InvalidOperationException("The check-in failed."));
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the screen's own continuation

        Assert.True(_crashed);
        Assert.Equal(["Unhandled exception from Return; reported to the shell"], _errorLog.Entries.Select(entry => entry.Message));
    }

    // While the screen is open, a failure goes to its ErrorBoundary, not to ErrorHub, and nothing chimes.
    [Fact]
    public async Task Scanned_CheckInFails_HandsItToTheErrorBoundary()
    {
        _desk.CheckingIn = Task.FromException<CheckIn>(new InvalidOperationException("The check-in failed."));
        var cut = Render<Return>();

        await ScanLabelAsync(cut);

        Assert.True(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
        Assert.Empty(_sounds.Played);
    }

    // A scan, as the scanner's thread raises it, then a turn of the screen's own thread, so the screen has
    // handled it before the test looks.
    private async Task ScanAsync(IRenderedComponent<Return> cut, ScanKind kind, string code)
    {
        _scanner.Emit(new Scan(kind, code));
        await cut.InvokeAsync(() => { });
    }

    private Task ScanLabelAsync(IRenderedComponent<Return> cut, string label = Label) => ScanAsync(cut, ScanKind.StoreLabel, label);

    // The screen with Halloween just checked in, asking who watched it.
    private async Task<IRenderedComponent<Return>> RenderAskingAsync()
    {
        var cut = Render<Return>();
        await ScanLabelAsync(cut);
        return cut;
    }

    private async Task CloseDialogAsync(IRenderedComponent<Return> cut)
    {
        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        await cut.InvokeAsync(() => { }); // queued behind the screen's own continuation
    }

    // Picks a choice by its words, from the list beside the person's name, as the clerk does.
    private static Task PickAsync(IRenderedComponent<Return> cut, MemberRow person, string choice)
    {
        var list = ListOf(cut, person);
        var value = list.QuerySelectorAll("option").Single(option => option.TextContent == choice).GetAttribute("value");
        return list.ChangeAsync(new ChangeEventArgs { Value = value });
    }

    // Clicks Save or Skip in the form.
    private static Task PressAsync(IRenderedComponent<Return> cut, string button) =>
        cut.FindAll(".who-watched button").Single(found => found.TextContent == button).ClickAsync(new MouseEventArgs());

    // Clicks Save, for a save that finishes at once. A dialog would wait for someone to close it, and the test
    // with it, so a dialog fails the test instead, as Checkout's PayAsync does.
    private async Task SaveAsync(IRenderedComponent<Return> cut)
    {
        var save = PressAsync(cut, "Save");
        Assert.Null(_dialogs.Current?.Message);
        await save;
    }

    // The list beside the person's name, found through the label's for, as a click on the name finds it.
    private static IElement ListOf(IRenderedComponent<Return> cut, MemberRow person) =>
        cut.Find($"#{cut.FindAll(".who-watched label").Single(label => label.TextContent == $"{person.Name}:").GetAttribute("for")}");

    private static IReadOnlyList<IElement> Lists(IRenderedComponent<Return> cut) => cut.FindAll(".who-watched select");

    // The choice a list shows: the one whose value Blazor gave the list.
    private static string Chosen(IElement list) =>
        list.QuerySelectorAll("option").Single(option => option.GetAttribute("value") == list.GetAttribute("value")).TextContent;

    private static string Prompt(IRenderedComponent<Return> cut) => cut.Find(".return-prompt").TextContent;

    // What happened to the last case scanned, or null when the screen says nothing about one.
    private static string? Last(IRenderedComponent<Return> cut) => cut.FindAll(".return-last").SingleOrDefault()?.TextContent;

    private static string AskingLine(IRenderedComponent<Return> cut) => cut.Find(".return-asking").TextContent;

    private void AssertWarning(string message)
    {
        var dialog = Assert.IsType<Dialog>(_dialogs.Current);
        Assert.Equal((message, DialogIcon.Warning), (dialog.Message, dialog.Icon));
    }

    private void AssertLoggedIgnored(string message) => Assert.Contains((LogLevel.Debug, message, (Exception?)null), _log.Entries);
}
