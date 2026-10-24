using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Rewind.Core.Devices;
using Rewind.Core.Intake;
using Rewind.Core.Metadata;
using Rewind.Core.Scans;
using Rewind.UI.Dialogs;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class IntakeTests : BunitContext
{
    private static readonly TmdbMatch Halloween1978 = new(948, "Halloween", 1978, "A masked killer comes home.", null);
    private static readonly TmdbMatch Halloween2018 = new(424139, "Halloween", 2018, "Forty years on.", null);

    private static readonly MovieCard Halloween = new(
        MovieId: 1,
        Title: "Halloween",
        Year: 1978,
        RuntimeMinutes: 91,
        Rating: "R",
        Director: "John Carpenter",
        Overview: "A masked killer comes home.",
        Cast: ["Donald Pleasence", "Jamie Lee Curtis", "Nancy Kyes", "P. J. Soles", "Charles Cyphers"],
        Genres: ["Horror", "Thriller"])
    {
        Poster = [0xFF, 0xD8, 0xFF, 0xE0],
    };

    // Tommy Boy on DVD, as UPCitemdb names it, and the movie TMDB has for it.
    private const string TommyBoyUpc = "032429258373";
    private const string UpcAsEan = "0032429258373";
    private const string HalloweenUpc = "036000291452";
    private static readonly BarcodeTitle TommyBoyFound = new(TommyBoyUpc, UpcStatus.Found, "Tommy Boy (DVD)", "Tommy Boy", null, null);
    private static readonly TmdbMatch TommyBoy1995 = new(11381, "Tommy Boy", 1995, "A party animal hits the road to save the family business.", null);

    private static readonly MovieCard TommyBoy = new(
        MovieId: 2,
        Title: "Tommy Boy",
        Year: 1995,
        RuntimeMinutes: 97,
        Rating: "PG-13",
        Director: "Peter Segal",
        Overview: "A party animal hits the road to save the family business.",
        Cast: ["Chris Farley", "David Spade"],
        Genres: ["Comedy"]);

    private readonly FakeMovieIntake _intake = new();
    private readonly FakeBarcodeTitles _titles = new();
    private readonly FakeScanner _scanner = new();
    private readonly DialogService _dialogs = new(new FakeSoundPlayer());

    // 6:15 PM on Saturday, October 24, 2026, on a register clock four hours behind UTC (TEST-4).
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 24, 22, 15, 0, TimeSpan.Zero));

    public IntakeTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Register", TimeSpan.FromHours(-4), "Register", "Register"));
        Services.AddSingleton<IMovieIntake>(_intake);
        Services.AddSingleton<IBarcodeTitles>(_titles);
        Services.AddSingleton<IScanner>(_scanner);
        Services.AddSingleton<TimeProvider>(_time);
        Services.AddSingleton(_dialogs);
    }

    // UI-3: the clerk can type a title as soon as the screen opens.
    [Fact]
    public void Render_ScreenOpens_FocusesTheTitleBox()
    {
        _ = Render<Intake>();

        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task Submit_Halloween_ListsEachMatchWithItsYear()
    {
        _intake.Search = Found(Halloween1978, Halloween2018);
        var cut = Render<Intake>();

        await SearchAsync(cut, " Halloween ");

        Assert.Equal(["Halloween"], _intake.Searches);
        Assert.Equal(["1978", "2018"], cut.FindAll(".tmdb-picker tbody tr").Select(row => row.Children[1].TextContent));
    }

    [Fact]
    public async Task Submit_BlankTitle_SearchesNothing()
    {
        var cut = Render<Intake>();

        await SearchAsync(cut, "   ");

        Assert.Empty(_intake.Searches);
    }

    // UI-6: the loading state. The search finishes only once TMDB answers, so it's awaited last.
    [Fact]
    public async Task Submit_TmdbStillAnswering_SaysItsSearching()
    {
        var answer = new TaskCompletionSource<TmdbSearch>(TaskCreationOptions.RunContinuationsAsynchronously);
        _intake.Search = answer.Task;
        var cut = Render<Intake>();

        var search = SearchAsync(cut, "Halloween");

        Assert.Equal("Searching TMDB...", cut.Find(".intake-prompt").TextContent);
        Assert.True(cut.Find("#intake-title").HasAttribute("disabled"));
        answer.SetResult(new TmdbSearch(TmdbStatus.Ok, []));
        await search;
    }

    // UI-6: the empty state.
    [Fact]
    public async Task Submit_NoMatches_SaysSo()
    {
        var cut = Render<Intake>();

        await SearchAsync(cut, "Zzyzx");

        Assert.Contains("TMDB has no movie called \"Zzyzx\"", cut.Find(".intake-prompt").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".tmdb-picker"));
    }

    // UI-6, GEN-7: the error states, as Windows 98 dialogs. The search finishes only once the
    // clerk closes the dialog, so it's awaited last.
    [Theory]
    [InlineData(TmdbStatus.TryLater, DialogIcon.Warning)]
    [InlineData(TmdbStatus.NotSetUp, DialogIcon.Error)]
    public async Task Submit_TmdbCantHelp_ShowsADialog(TmdbStatus status, DialogIcon icon)
    {
        _intake.Search = Task.FromResult(new TmdbSearch(status, []));
        var cut = Render<Intake>();

        var search = SearchAsync(cut, "Halloween");

        Assert.Equal(icon, _dialogs.Current?.Icon);
        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        await search;
    }

    // Behind the dialog, the screen shows what happened, not that it's still searching.
    [Fact]
    public async Task Submit_TmdbAnswersLateWithTrouble_StopsSayingItsSearching()
    {
        var answer = new TaskCompletionSource<TmdbSearch>(TaskCreationOptions.RunContinuationsAsynchronously);
        _intake.Search = answer.Task;
        var cut = Render<Intake>();
        var search = SearchAsync(cut, "Halloween");

        answer.SetResult(new TmdbSearch(TmdbStatus.TryLater, []));

        cut.WaitForAssertion(() => Assert.NotNull(_dialogs.Current));
        Assert.Equal("Scan the DVD's barcode, or type its title and press Enter.", cut.Find(".intake-prompt").TextContent);
        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        await search;
    }

    // UI-10: TMDB's text is shown as text, never as markup.
    [Fact]
    public async Task Submit_StoryWithMarkup_ShowsItAsText()
    {
        _intake.Search = Found(Halloween1978 with { Overview = "<b>Boo!</b>" });
        var cut = Render<Intake>();

        await SearchAsync(cut, "Halloween");

        Assert.Equal("<b>Boo!</b>", cut.Find(".tmdb-picker-story").TextContent);
        Assert.Empty(cut.FindAll(".tmdb-picker-story b"));
    }

    [Fact]
    public async Task KeyDownEnter_OnTheFirstMatch_AddsIt()
    {
        _intake.Search = Found(Halloween1978, Halloween2018);
        _intake.Add = Task.FromResult(new MovieAdd(AddStatus.Added, Halloween));
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");

        await cut.Find(".tmdb-picker tbody tr").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal([948], _intake.Adds);
    }

    // Two adds of one movie at once would both find it missing, and the second save would break
    // Movies.TmdbId's unique index.
    [Fact]
    public async Task KeyDownEnter_TwiceWhileAdding_AddsOnce()
    {
        var added = new TaskCompletionSource<MovieAdd>(TaskCreationOptions.RunContinuationsAsynchronously);
        _intake.Search = Found(Halloween1978);
        _intake.Add = added.Task;
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");
        var first = PickFirstAsync(cut);
        var second = PickFirstAsync(cut);

        added.SetResult(new MovieAdd(AddStatus.Added, Halloween));
        await Task.WhenAll(first, second);

        Assert.Equal([948], _intake.Adds);
    }

    // The Build Plan 2.3 check, as the clerk sees it.
    [Fact]
    public async Task Pick_Halloween_ShowsItsRatingDirectorAndCast()
    {
        _intake.Search = Found(Halloween1978);
        _intake.Add = Task.FromResult(new MovieAdd(AddStatus.Added, Halloween));
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");

        await PickFirstAsync(cut);

        var movie = cut.Find(".intake-movie");
        Assert.Equal("Halloween (1978)", movie.QuerySelector("legend")?.TextContent);
        Assert.Contains("Rated R · 91 minutes · Horror, Thriller", movie.TextContent, StringComparison.Ordinal);
        Assert.Contains("Directed by John Carpenter", movie.TextContent, StringComparison.Ordinal);
        Assert.Contains(
            "Starring Donald Pleasence, Jamie Lee Curtis, Nancy Kyes, P. J. Soles, Charles Cyphers",
            movie.TextContent,
            StringComparison.Ordinal);
        Assert.Equal("data:image/jpeg;base64,/9j/4A==", movie.QuerySelector("img")?.GetAttribute("src"));
    }

    [Fact]
    public async Task Pick_MovieAdded_ClearsTheTitleForTheNextOne()
    {
        _intake.Search = Found(Halloween1978);
        _intake.Add = Task.FromResult(new MovieAdd(AddStatus.Added, Halloween));
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");

        await PickFirstAsync(cut);

        Assert.Equal(
            "Added to the collection. Scan the next DVD, or type its title and press Enter.",
            cut.Find(".intake-prompt").TextContent);
        Assert.Equal("", cut.Find("#intake-title").GetAttribute("value") ?? "");
    }

    [Fact]
    public async Task Pick_MovieAlreadyOwned_SaysSo()
    {
        _intake.Search = Found(Halloween1978);
        _intake.Add = Task.FromResult(new MovieAdd(AddStatus.AlreadyOwned, Halloween));
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");

        await PickFirstAsync(cut);

        Assert.Equal(
            "That movie is already in the collection. Scan the next DVD, or type its title and press Enter.",
            cut.Find(".intake-prompt").TextContent);
    }

    // The picker stays, so the clerk can try again or pick another match.
    [Fact]
    public async Task Pick_TmdbDoesNotAnswer_KeepsThePickerOpen()
    {
        _intake.Search = Found(Halloween1978);
        _intake.Add = Task.FromResult(new MovieAdd(AddStatus.TryLater, null));
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");

        var pick = PickFirstAsync(cut);

        Assert.Equal(DialogIcon.Warning, _dialogs.Current?.Icon);
        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        await pick;
        Assert.Single(cut.FindAll(".tmdb-picker"));
    }

    [Fact]
    public async Task KeyDownEscape_InThePicker_ClosesIt()
    {
        _intake.Search = Found(Halloween1978);
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");

        await cut.Find(".tmdb-picker").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".tmdb-picker"));
        Assert.Empty(_intake.Adds);
    }

    // UI-3: Esc waits for a pick being added, as Enter does. Closing the list would look like a
    // cancel, and then the movie would appear anyway.
    [Fact]
    public async Task KeyDownEscape_WhileAdding_KeepsThePickerOpen()
    {
        var (cut, added, pick) = await StartAddingAsync();

        await cut.Find(".tmdb-picker").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Single(cut.FindAll(".tmdb-picker"));
        added.SetResult(new MovieAdd(AddStatus.Added, Halloween));
        await pick;
    }

    [Fact]
    public async Task ClickCancel_WhileAdding_KeepsThePickerOpen()
    {
        var (cut, added, pick) = await StartAddingAsync();

        await cut.FindAll(".tmdb-picker button").Single(button => button.TextContent == "Cancel").ClickAsync(new MouseEventArgs());

        Assert.Single(cut.FindAll(".tmdb-picker"));
        added.SetResult(new MovieAdd(AddStatus.Added, Halloween));
        await pick;
    }

    // UI-3: Esc closes the picker wherever the focus is on the screen.
    [Fact]
    public async Task KeyDownEscape_InTheTitleBox_ClosesThePicker()
    {
        _intake.Search = Found(Halloween1978);
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");

        await cut.Find("#intake-title").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".tmdb-picker"));
        Assert.Equal("Scan the DVD's barcode, or type its title and press Enter.", cut.Find(".intake-prompt").TextContent);
    }

    // Build Plan 2.2's check, as the clerk sees it: the scan puts a clean title in the box and
    // searches for it, as Enter would.
    [Fact]
    public async Task Scanned_UpcWithATitle_SearchesForItFromTheTitleBox()
    {
        _titles.Lookup = Task.FromResult(TommyBoyFound);
        _intake.Search = Found(TommyBoy1995);
        var cut = Render<Intake>();

        await ScanAsync(cut, TommyBoyUpc);

        Assert.Equal([TommyBoyUpc], _titles.Lookups);
        Assert.Equal("Tommy Boy", cut.Find("#intake-title").GetAttribute("value"));
        Assert.Equal(["Tommy Boy"], _intake.Searches);
    }

    [Fact]
    public async Task Scanned_UpcWithATitle_OpensThePickerSayingWhatItFound()
    {
        _titles.Lookup = Task.FromResult(TommyBoyFound);
        _intake.Search = Found(TommyBoy1995);
        var cut = Render<Intake>();

        await ScanAsync(cut, TommyBoyUpc);

        Assert.Single(cut.FindAll(".tmdb-picker"));
        Assert.Equal("Found Tommy Boy. Pick the movie, or press Esc to fix the title.", cut.Find(".intake-prompt").TextContent);
    }

    // UI-10: UPCitemdb's text is shown as text, never as markup.
    [Fact]
    public async Task Scanned_TitleWithMarkup_ShowsItAsText()
    {
        _titles.Lookup = Task.FromResult(TommyBoyFound with { Title = "<b>Tommy</b>" });
        _intake.Search = Found(TommyBoy1995);
        var cut = Render<Intake>();

        await ScanAsync(cut, TommyBoyUpc);

        Assert.Contains("Found <b>Tommy</b>.", cut.Find(".intake-prompt").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".intake-prompt b"));
    }

    // UI-6: the loading state, which can last while UPCitemdb answers. The title box is read-only,
    // not disabled, so Esc can still stop the lookup.
    [Fact]
    public async Task Scanned_UpcItemDbStillAnswering_SaysItsLookingItUp()
    {
        var answer = new TaskCompletionSource<BarcodeTitle>(TaskCreationOptions.RunContinuationsAsynchronously);
        _titles.Lookup = answer.Task;
        var cut = Render<Intake>();

        await ScanAsync(cut, TommyBoyUpc);

        Assert.Equal("Looking up 032429258373... Press Esc to type the title instead.", cut.Find(".intake-prompt").TextContent);
        Assert.True(cut.Find("#intake-title").HasAttribute("readonly"));
        Assert.False(cut.Find("#intake-title").HasAttribute("disabled"));
        answer.SetResult(TommyBoyFound);
        cut.WaitForAssertion(() => Assert.False(cut.Find("#intake-title").HasAttribute("disabled")));
    }

    // UI-3: the title box has the focus during a lookup, so Esc reaches the screen.
    [Fact]
    public async Task Scanned_Upc_FocusesTheTitleBoxForEsc()
    {
        _titles.Lookup = new TaskCompletionSource<BarcodeTitle>().Task;
        var cut = Render<Intake>();
        var titleBox = TitleBoxId(cut);

        await ScanAsync(cut, TommyBoyUpc);

        AssertFocusedLast(titleBox, focuses: 2);
    }

    // The pacer's word that the lookup waits for UPCitemdb's burst limit shows, until it's asking.
    [Fact]
    public async Task Scanned_LookupWaitsItsTurn_SaysForHowLong()
    {
        _titles.Lookup = new TaskCompletionSource<BarcodeTitle>().Task;
        var cut = Render<Intake>();
        await ScanAsync(cut, TommyBoyUpc);

        _titles.LastWaiting?.Report(TimeSpan.FromSeconds(6.2));

        cut.WaitForAssertion(() => Assert.Equal(
            "Looking up 032429258373 in 7 seconds... Press Esc to type the title instead.",
            cut.Find(".intake-prompt").TextContent));
        _titles.LastWaiting?.Report(TimeSpan.Zero);
        cut.WaitForAssertion(() => Assert.Equal(
            "Looking up 032429258373... Press Esc to type the title instead.",
            cut.Find(".intake-prompt").TextContent));
    }

    // A report of a wait that arrives once its lookup has ended changes nothing, or the screen would
    // say it's looking up, with the title box disabled, for good.
    [Fact]
    public async Task Scanned_WaitReportedAfterTheLookup_ChangesNothing()
    {
        var cut = Render<Intake>();
        await ScanAsync(cut, TommyBoyUpc);

        _titles.LastWaiting?.Report(TimeSpan.FromSeconds(7));
        await cut.InvokeAsync(() => { }); // after the report, which Progress posts to the screen's thread

        Assert.Equal("No title for that barcode. Type the title, then press Enter.", cut.Find(".intake-prompt").TextContent);
        Assert.False(cut.Find("#intake-title").HasAttribute("disabled"));
    }

    // Esc stops a lookup, such as one waiting its turn, and the clerk types the title instead. It's
    // still the same DVD, so the barcode stays.
    [Fact]
    public async Task KeyDownEscape_WhileLookingUp_StopsTheLookup()
    {
        _titles.Lookup = new TaskCompletionSource<BarcodeTitle>().Task;
        var cut = Render<Intake>();
        await ScanAsync(cut, TommyBoyUpc);

        await cut.Find("#intake-title").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        cut.WaitForAssertion(() => Assert.Equal("Type the title, then press Enter.", cut.Find(".intake-prompt").TextContent));
        Assert.True(_titles.LastToken.IsCancellationRequested);
        Assert.False(cut.Find("#intake-title").HasAttribute("readonly"));
        Assert.Equal("This DVD's barcode: 032429258373", cut.Find(".intake-barcode").TextContent);
    }

    // UI-6: the empty state. Whatever was in the title box belonged to the DVD before.
    [Fact]
    public async Task Scanned_UpcWithNoTitle_EmptiesTheTitleBoxAndFocusesIt()
    {
        var cut = Render<Intake>();
        var titleBox = TitleBoxId(cut);
        await cut.Find("#intake-title").InputAsync("Halloween");

        await ScanAsync(cut, TommyBoyUpc);

        Assert.Equal("No title for that barcode. Type the title, then press Enter.", cut.Find(".intake-prompt").TextContent);
        Assert.Equal("", cut.Find("#intake-title").GetAttribute("value") ?? "");
        AssertFocusedLast(titleBox, focuses: 3);
    }

    // A code that isn't a factory barcode after all didn't scan properly; it isn't a DVD UPCitemdb
    // doesn't know.
    [Fact]
    public async Task Scanned_NotAFactoryBarcode_SaysToScanAgain()
    {
        _titles.Lookup = Task.FromResult(new BarcodeTitle(null, UpcStatus.NotFound, null, null, null, null));
        var cut = Render<Intake>();

        await ScanAsync(cut, TommyBoyUpc);

        Assert.Equal("That barcode didn't read properly. Scan it again.", cut.Find(".intake-prompt").TextContent);
        Assert.Empty(cut.FindAll(".intake-barcode"));
    }

    // UI-6, NET-12: the error states are expected, so a prompt line says what to do, not a dialog.
    [Theory]
    [InlineData(UpcTryLaterReason.TooFast, "UPCitemdb is busy. Type the title, then press Enter.")]
    [InlineData(UpcTryLaterReason.ServerError, "UPCitemdb is busy. Type the title, then press Enter.")]
    [InlineData(UpcTryLaterReason.Offline, "UPCitemdb didn't answer. Type the title, then press Enter.")]
    [InlineData(UpcTryLaterReason.DailyLimit, "UPCitemdb is out of lookups for today. Type the title, then press Enter.")]
    public async Task Scanned_UpcItemDbCannotAnswer_AsksForTheTitle(UpcTryLaterReason reason, string prompt)
    {
        _titles.Lookup = Task.FromResult(new BarcodeTitle(TommyBoyUpc, UpcStatus.TryLater, null, null, reason, null));
        var cut = Render<Intake>();
        var titleBox = TitleBoxId(cut);

        await ScanAsync(cut, TommyBoyUpc);

        Assert.Equal(prompt, cut.Find(".intake-prompt").TextContent);
        Assert.Null(_dialogs.Current);
        AssertFocusedLast(titleBox, focuses: 3);
    }

    // When UPCitemdb said its lookups come back, in the register's own time, where it's 6:15 PM.
    [Theory]
    [InlineData(0.75, "UPCitemdb is out of lookups until 7:00 PM. Type the title, then press Enter.")]
    [InlineData(24.75, "UPCitemdb is out of lookups until 7:00 PM tomorrow. Type the title, then press Enter.")]
    [InlineData(48.75, "UPCitemdb is out of lookups until Monday at 7:00 PM. Type the title, then press Enter.")]
    public async Task Scanned_OutOfLookups_SaysWhenTheyComeBack(double hours, string prompt)
    {
        var back = _time.GetUtcNow() + TimeSpan.FromHours(hours);
        _titles.Lookup = Task.FromResult(new BarcodeTitle(TommyBoyUpc, UpcStatus.TryLater, null, null, UpcTryLaterReason.DailyLimit, back));
        var cut = Render<Intake>();

        await ScanAsync(cut, TommyBoyUpc);

        Assert.Equal(prompt, cut.Find(".intake-prompt").TextContent);
    }

    // The clerk didn't spell a title UPCitemdb gave, so it's to fix, not to check.
    [Fact]
    public async Task Scanned_TitleTmdbHasNoMovieFor_SaysToFixTheTitle()
    {
        _titles.Lookup = Task.FromResult(TommyBoyFound with { Title = "Tommy Boy Bonus" });
        var cut = Render<Intake>();

        await ScanAsync(cut, TommyBoyUpc);

        Assert.Equal("TMDB has no movie called \"Tommy Boy Bonus\". Fix the title, then search again.", cut.Find(".intake-prompt").TextContent);
    }

    // The barcode the screen keeps is Core's form of it, which UpcLookups and Case.FactoryUpc use.
    [Fact]
    public async Task Scanned_Upc_KeepsCoresFormOfTheBarcode()
    {
        _titles.Lookup = Task.FromResult(TommyBoyFound);
        var cut = Render<Intake>();

        await ScanAsync(cut, UpcAsEan);

        Assert.Equal("This DVD's barcode: 032429258373", cut.Find(".intake-barcode").TextContent);
    }

    [Fact]
    public async Task Scanned_Upc_ShowsTheBarcode()
    {
        var cut = Render<Intake>();

        await ScanAsync(cut, TommyBoyUpc);

        Assert.Equal("This DVD's barcode: 032429258373", cut.Find(".intake-barcode").TextContent);
    }

    // Build Plan 2.4 saves the barcode on the case once the movie is added, so it stays until then.
    [Fact]
    public async Task Pick_AfterAScan_KeepsTheBarcode()
    {
        _titles.Lookup = Task.FromResult(TommyBoyFound);
        _intake.Search = Found(TommyBoy1995);
        _intake.Add = Task.FromResult(new MovieAdd(AddStatus.Added, TommyBoy));
        var cut = Render<Intake>();
        await ScanAsync(cut, TommyBoyUpc);

        await PickFirstAsync(cut);

        Assert.Single(cut.FindAll(".intake-movie"));
        Assert.Equal("This DVD's barcode: 032429258373", cut.Find(".intake-barcode").TextContent);
    }

    [Fact]
    public async Task Scanned_SecondUpc_ReplacesTheBarcode()
    {
        var cut = Render<Intake>();
        await ScanAsync(cut, TommyBoyUpc);

        await ScanAsync(cut, HalloweenUpc);

        Assert.Equal("This DVD's barcode: 036000291452", cut.Find(".intake-barcode").TextContent);
    }

    // A wrong title from UPCitemdb is fixed as a typed one is (Build Plan 2.3), and it's still the
    // same DVD, so the barcode stays.
    [Fact]
    public async Task KeyDownEscape_AfterAScan_LeavesTheTitleToFix()
    {
        _titles.Lookup = Task.FromResult(TommyBoyFound);
        _intake.Search = Found(TommyBoy1995);
        var cut = Render<Intake>();
        await ScanAsync(cut, TommyBoyUpc);

        await cut.Find(".tmdb-picker").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".tmdb-picker"));
        Assert.Equal("Tommy Boy", cut.Find("#intake-title").GetAttribute("value"));
        Assert.Equal("Fix the title, then press Enter.", cut.Find(".intake-prompt").TextContent);
    }

    [Fact]
    public async Task Submit_TitleFixedAfterAScan_KeepsTheBarcode()
    {
        _titles.Lookup = Task.FromResult(TommyBoyFound);
        _intake.Search = Found(TommyBoy1995);
        var cut = Render<Intake>();
        await ScanAsync(cut, TommyBoyUpc);
        await cut.Find(".tmdb-picker").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        await SearchAsync(cut, "Tommy Boy 1995");

        Assert.Equal(["Tommy Boy", "Tommy Boy 1995"], _intake.Searches);
        Assert.Equal("This DVD's barcode: 032429258373", cut.Find(".intake-barcode").TextContent);
    }

    // A title typed once a movie is added is the next DVD's, which wasn't scanned.
    [Fact]
    public async Task Submit_TitleTypedAfterAnAdd_HasNoBarcode()
    {
        _titles.Lookup = Task.FromResult(TommyBoyFound);
        _intake.Search = Found(TommyBoy1995);
        _intake.Add = Task.FromResult(new MovieAdd(AddStatus.Added, TommyBoy));
        var cut = Render<Intake>();
        await ScanAsync(cut, TommyBoyUpc);
        await PickFirstAsync(cut);

        await SearchAsync(cut, "Halloween");

        Assert.Empty(cut.FindAll(".intake-barcode"));
    }

    // A title typed after a scan names the scanned DVD, as it must when UPCitemdb has no title for
    // it, so closing its list leaves the barcode waiting for the title.
    [Fact]
    public async Task KeyDownEscape_ListOfATitleTypedAfterAScan_KeepsTheBarcode()
    {
        _intake.Search = Found(Halloween1978);
        var cut = Render<Intake>();
        await ScanAsync(cut, TommyBoyUpc);
        await SearchAsync(cut, "Halloween");

        await cut.Find("#intake-title").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".tmdb-picker"));
        Assert.Equal("This DVD's barcode: 032429258373", cut.Find(".intake-barcode").TextContent);
        Assert.Equal("Type the title, then press Enter.", cut.Find(".intake-prompt").TextContent);
    }

    // UI-3: with nothing to stop or close, Esc starts over, so a DVD scanned by mistake doesn't
    // lend its barcode to the next title typed.
    [Fact]
    public async Task KeyDownEscape_NothingToStopOrClose_StartsOver()
    {
        var cut = Render<Intake>();
        var titleBox = TitleBoxId(cut);
        await ScanAsync(cut, TommyBoyUpc);
        await cut.Find("#intake-title").InputAsync("Halloween");

        await cut.Find("#intake-title").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".intake-barcode"));
        Assert.Equal("", cut.Find("#intake-title").GetAttribute("value") ?? "");
        Assert.Equal("Scan the DVD's barcode, or type its title and press Enter.", cut.Find(".intake-prompt").TextContent);
        AssertFocusedLast(titleBox, focuses: 4);
    }

    // The clerk's own report: a title typed after the wrong DVD's scan. Esc closes the list, then
    // starts over, and the title searched next has no barcode.
    [Fact]
    public async Task KeyDownEscape_TwiceAfterATitleTypedForTheWrongScan_DropsTheBarcode()
    {
        _intake.Search = Found(Halloween1978);
        var cut = Render<Intake>();
        await ScanAsync(cut, TommyBoyUpc);
        await SearchAsync(cut, "Halloween");
        await cut.Find("#intake-title").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        await cut.Find("#intake-title").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        await SearchAsync(cut, "Halloween");

        Assert.Single(cut.FindAll(".tmdb-picker"));
        Assert.Empty(cut.FindAll(".intake-barcode"));
    }

    // After an add, starting over clears the movie too, back to how the screen opens.
    [Fact]
    public async Task KeyDownEscape_AfterAnAdd_StartsOver()
    {
        _titles.Lookup = Task.FromResult(TommyBoyFound);
        _intake.Search = Found(TommyBoy1995);
        _intake.Add = Task.FromResult(new MovieAdd(AddStatus.Added, TommyBoy));
        var cut = Render<Intake>();
        await ScanAsync(cut, TommyBoyUpc);
        await PickFirstAsync(cut);

        await cut.Find("#intake-title").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".intake-movie"));
        Assert.Empty(cut.FindAll(".intake-barcode"));
        Assert.Equal("Scan the DVD's barcode, or type its title and press Enter.", cut.Find(".intake-prompt").TextContent);
    }

    // UI-3: not while TMDB is searching, as Esc doesn't close the list while a pick is being added:
    // the search's answer would land on a screen that had started over.
    [Fact]
    public async Task KeyDownEscape_WhileSearching_KeepsTheDvd()
    {
        var search = new TaskCompletionSource<TmdbSearch>(TaskCreationOptions.RunContinuationsAsynchronously);
        _titles.Lookup = Task.FromResult(TommyBoyFound);
        _intake.Search = search.Task;
        var cut = Render<Intake>();
        await ScanAsync(cut, TommyBoyUpc);

        await cut.Find("form").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        search.SetResult(new TmdbSearch(TmdbStatus.Ok, [TommyBoy1995]));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".tmdb-picker")));
        Assert.Equal("This DVD's barcode: 032429258373", cut.Find(".intake-barcode").TextContent);
        Assert.Equal("Found Tommy Boy. Pick the movie, or press Esc to fix the title.", cut.Find(".intake-prompt").TextContent);
    }

    // One DVD at a time: a scan in the middle of an add would start the next DVD before this one
    // is done.
    [Fact]
    public async Task Scanned_WhileAdding_IsIgnored()
    {
        var (cut, added, pick) = await StartAddingAsync();

        await ScanAsync(cut, TommyBoyUpc);

        Assert.Empty(_titles.Lookups);
        Assert.Empty(cut.FindAll(".intake-barcode"));
        added.SetResult(new MovieAdd(AddStatus.Added, Halloween));
        await pick;
    }

    [Fact]
    public async Task Scanned_WhileLookingUp_IsIgnored()
    {
        var answer = new TaskCompletionSource<BarcodeTitle>(TaskCreationOptions.RunContinuationsAsynchronously);
        _titles.Lookup = answer.Task;
        var cut = Render<Intake>();
        await ScanAsync(cut, TommyBoyUpc);

        await ScanAsync(cut, HalloweenUpc);

        Assert.Equal([TommyBoyUpc], _titles.Lookups);
        Assert.Equal("This DVD's barcode: 032429258373", cut.Find(".intake-barcode").TextContent);
        answer.SetResult(TommyBoyFound);
        cut.WaitForAssertion(() => Assert.False(cut.Find("#intake-title").HasAttribute("disabled")));
    }

    // UI-5: as in Windows 98, an open dialog is answered first.
    [Fact]
    public async Task Scanned_WhileADialogIsOpen_IsIgnored()
    {
        _intake.Search = Task.FromResult(new TmdbSearch(TmdbStatus.TryLater, []));
        var cut = Render<Intake>();
        var search = SearchAsync(cut, "Halloween");

        await ScanAsync(cut, TommyBoyUpc);

        Assert.NotNull(_dialogs.Current);
        Assert.Empty(_titles.Lookups);
        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        await search;
    }

    // Store labels are Build Plan 2.4's, and nothing else means anything on this screen yet.
    [Theory]
    [InlineData(ScanKind.StoreLabel, "RV-00042")]
    [InlineData(ScanKind.MemberCard, "MEM-001")]
    [InlineData(ScanKind.Unknown, "HELLO")]
    public async Task Scanned_AnythingButAUpc_DoesNothing(ScanKind kind, string code)
    {
        var cut = Render<Intake>();

        _scanner.Emit(new Scan(kind, code));
        await cut.InvokeAsync(() => { });

        Assert.Empty(_titles.Lookups);
        Assert.Empty(cut.FindAll(".intake-barcode"));
        Assert.Equal("Scan the DVD's barcode, or type its title and press Enter.", cut.Find(".intake-prompt").TextContent);
    }

    // The scanner outlives the screen, so the screen stops listening when the clerk leaves it.
    [Fact]
    public async Task Dispose_ClerkLeavesTheScreen_StopsListeningForScans()
    {
        _ = Render<Intake>();

        await DisposeComponentsAsync();

        Assert.False(_scanner.IsListenedTo);
    }

    // A lookup still waiting its turn when the clerk leaves stops. Its answer never comes here.
    [Fact]
    public async Task Dispose_WhileLookingUp_CancelsTheLookup()
    {
        _titles.Lookup = new TaskCompletionSource<BarcodeTitle>().Task;
        var cut = Render<Intake>();
        await ScanAsync(cut, TommyBoyUpc);

        await DisposeComponentsAsync();

        Assert.True(_titles.LastToken.IsCancellationRequested);
    }

    private static Task<TmdbSearch> Found(params TmdbMatch[] matches) =>
        Task.FromResult(new TmdbSearch(TmdbStatus.Ok, matches));

    // A factory UPC, as the scanner's thread would raise it, then a turn of the screen's own
    // thread, so the screen has handled it before the test looks.
    private async Task ScanAsync(IRenderedComponent<Intake> cut, string upc)
    {
        _scanner.Emit(new Scan(ScanKind.Upc, upc));
        await cut.InvokeAsync(() => { });
    }

    // The ID of the title box's @ref. bUnit writes it into the markup only on the first render, and
    // as "" after that, so a test reads it before anything else happens.
    private static string? TitleBoxId(IRenderedComponent<Intake> cut) =>
        cut.Find("#intake-title").GetAttribute("blazor:elementreference");

    // UI-3: the element the screen focused last, from its focus calls so far.
    private void AssertFocusedLast(string? elementId, int focuses)
    {
        var focused = Assert.IsType<ElementReference>(JSInterop.VerifyFocusAsyncInvoke(focuses)[^1].Arguments[0]);
        Assert.Equal(elementId, focused.Id);
    }

    private static async Task SearchAsync(IRenderedComponent<Intake> cut, string title)
    {
        await cut.Find("#intake-title").InputAsync(title);
        await cut.Find("form").SubmitAsync();
    }

    private static Task PickFirstAsync(IRenderedComponent<Intake> cut) =>
        cut.Find(".tmdb-picker tbody tr").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

    // Searches, then picks Halloween (1978), whose add waits until the test sets its answer.
    private async Task<(IRenderedComponent<Intake> Cut, TaskCompletionSource<MovieAdd> Added, Task Pick)> StartAddingAsync()
    {
        var added = new TaskCompletionSource<MovieAdd>(TaskCreationOptions.RunContinuationsAsynchronously);
        _intake.Search = Found(Halloween1978);
        _intake.Add = added.Task;
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");
        return (cut, added, PickFirstAsync(cut));
    }
}
