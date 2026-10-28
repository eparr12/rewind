using System.Diagnostics;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Rewind.Core.Devices;
using Rewind.Core.Intake;
using Rewind.Core.Metadata;
using Rewind.Core.Scans;
using Rewind.Core.Sounds;
using Rewind.UI.Dialogs;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class IntakeTests : BunitContext
{
    private static readonly TmdbMatch Halloween1978 = new(948, "Halloween", 1978, "A masked killer comes home.", null);
    private static readonly TmdbMatch Halloween2018 = new(424139, "Halloween", 2018, "Forty years on.", null);

    // When TMDB answered each pick, which the pick holds until the label scan saves it.
    private static readonly DateTime PickedAt = new(2026, 10, 24, 22, 15, 0, DateTimeKind.Utc);

    // Halloween (1978) as its pick holds it: new to the collection, fetched from TMDB with its poster.
    private static readonly CaseMovieEntry Halloween = new CaseMovieEntry.Fetched(
        new TmdbMovie(
            TmdbId: 948,
            Title: "Halloween",
            Year: 1978,
            RuntimeMinutes: 91,
            Rating: "R",
            Director: "John Carpenter",
            Overview: "A masked killer comes home.",
            PosterPath: "/wijlZ3HaYMvlDTPqJoTCWKFkCPU.jpg",
            Cast: ["Donald Pleasence", "Jamie Lee Curtis", "Nancy Kyes", "P. J. Soles", "Charles Cyphers"],
            Genres: [new TmdbGenre(27, "Horror"), new TmdbGenre(53, "Thriller")]),
        new Dictionary<PosterSize, byte[]> { [PosterSize.Register] = [0xFF, 0xD8, 0xFF, 0xE0] },
        PickedAt);

    // Halloween (1978) once the collection has it, as a second edition's pick finds it.
    private static readonly CaseMovieEntry HalloweenOwned = new CaseMovieEntry.Owned(
        MovieId: 1,
        TmdbId: 948,
        Title: "Halloween",
        Year: 1978,
        RuntimeMinutes: 91,
        Rating: "R",
        Director: "John Carpenter",
        Cast: ["Donald Pleasence", "Jamie Lee Curtis", "Nancy Kyes", "P. J. Soles", "Charles Cyphers"],
        Genres: ["Horror", "Thriller"],
        Poster: [0xFF, 0xD8, 0xFF, 0xE0]);

    // Its 2018 sequel, new to the collection, without a poster.
    private static readonly CaseMovieEntry HalloweenSequel = new CaseMovieEntry.Fetched(
        new TmdbMovie(424139, "Halloween", 2018, 106, "R", "David Gordon Green", "Forty years on.", null, ["Jamie Lee Curtis"], [new TmdbGenre(27, "Horror")]),
        new Dictionary<PosterSize, byte[]>(),
        PickedAt);

    // Tommy Boy on DVD, as UPCitemdb names it, and the movie TMDB has for it.
    private const string TommyBoyUpc = "032429258373";
    private const string UpcAsEan = "0032429258373";
    private const string HalloweenUpc = "036000291452";
    private static readonly BarcodeTitle TommyBoyFound = new(TommyBoyUpc, UpcStatus.Found, "Tommy Boy (DVD)", "Tommy Boy", null, null);
    private static readonly TmdbMatch TommyBoy1995 = new(11381, "Tommy Boy", 1995, "A party animal hits the road to save the family business.", null);

    private static readonly CaseMovieEntry TommyBoy = new CaseMovieEntry.Fetched(
        new TmdbMovie(
            TmdbId: 11381,
            Title: "Tommy Boy",
            Year: 1995,
            RuntimeMinutes: 97,
            Rating: "PG-13",
            Director: "Peter Segal",
            Overview: "A party animal hits the road to save the family business.",
            PosterPath: null,
            Cast: ["Chris Farley", "David Spade"],
            Genres: [new TmdbGenre(35, "Comedy")]),
        new Dictionary<PosterSize, byte[]>(),
        PickedAt);

    // The next two store labels on the sheet.
    private const string Label = "RV-00042";
    private const string SecondLabel = "RV-00043";

    // What the prompt says once the case holds a movie: what to do next.
    private const string LabelPrompt =
        "Peel the next store label, stick it on the case and scan it. Box set? Type the next movie's title first.";

    private readonly FakeMovieIntake _intake = new();
    private readonly FakeBarcodeTitles _titles = new();
    private readonly FakeScanner _scanner = new();
    private readonly FakeSoundPlayer _sounds = new();
    private readonly DialogService _dialogs;

    // 6:15 PM on Saturday, October 24, 2026, on a register clock four hours behind UTC (TEST-4).
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 24, 22, 15, 0, TimeSpan.Zero));

    public IntakeTests()
    {
        _dialogs = new DialogService(_sounds);
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Register", TimeSpan.FromHours(-4), "Register", "Register"));
        Services.AddSingleton<IMovieIntake>(_intake);
        Services.AddSingleton<IBarcodeTitles>(_titles);
        Services.AddSingleton<IScanner>(_scanner);
        Services.AddSingleton<ISoundPlayer>(_sounds);
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

    // Build Plan 2.6: the cases Core counts on the shelf, to check against the real shelves. None is
    // a count too, for a store that hasn't shelved a case yet.
    [Theory]
    [InlineData(0, "Cases on the shelf: 0")]
    [InlineData(6, "Cases on the shelf: 6")]
    public void Render_ScreenOpens_ShowsTheCasesOnTheShelf(int onShelf, string line)
    {
        _intake.OnShelf = Task.FromResult(onShelf);

        var cut = Render<Intake>();

        Assert.Equal(line, cut.Find(".intake-count").TextContent);
    }

    // The screen opens before Core has counted, and shows the count once it has.
    [Fact]
    public void Render_CountNotReadYet_ShowsItOnceRead()
    {
        var counted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _intake.OnShelf = counted.Task;
        var cut = Render<Intake>();
        Assert.Empty(cut.FindAll(".intake-count"));

        counted.SetResult(6);

        cut.WaitForAssertion(() => Assert.Equal("Cases on the shelf: 6", cut.Find(".intake-count").TextContent));
    }

    // UI-4: the screen listens before it counts, so a DVD scanned as it opens is looked up.
    [Fact]
    public async Task Scanned_WhileCounting_LooksTheBarcodeUp()
    {
        _intake.OnShelf = new TaskCompletionSource<int>().Task;
        _titles.Lookup = Task.FromResult(TommyBoyFound);
        var cut = Render<Intake>();

        await ScanAsync(cut, TommyBoyUpc);

        Assert.Equal([TommyBoyUpc], _titles.Lookups);
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
    public async Task KeyDownEnter_OnTheFirstMatch_PicksIt()
    {
        _intake.Search = Found(Halloween1978, Halloween2018);
        _intake.Pick = Picked(Halloween);
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");

        await cut.Find(".tmdb-picker tbody tr").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal([948], _intake.Picks);
    }

    // Two picks of one movie at once would both find the case without it, and it would be listed
    // twice.
    [Fact]
    public async Task KeyDownEnter_TwiceWhilePicking_PicksOnce()
    {
        var picked = new TaskCompletionSource<MoviePick>(TaskCreationOptions.RunContinuationsAsynchronously);
        _intake.Search = Found(Halloween1978);
        _intake.Pick = picked.Task;
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");
        var first = PickFirstAsync(cut);
        var second = PickFirstAsync(cut);

        picked.SetResult(new MoviePick(PickStatus.Fetched, Halloween));
        await Task.WhenAll(first, second);

        Assert.Equal([948], _intake.Picks);
    }

    // The Build Plan 2.3 check, as the clerk sees it before the label scan saves it (ADR-0005).
    [Fact]
    public async Task Pick_Halloween_ShowsItsRatingDirectorAndCast()
    {
        _intake.Search = Found(Halloween1978);
        _intake.Pick = Picked(Halloween);
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");

        await PickFirstAsync(cut);

        var movie = cut.Find(".case-contents .case-movie");
        Assert.Equal("Halloween (1978)", movie.QuerySelector(".case-movie-title")?.TextContent);
        Assert.Contains("Rated R · 91 minutes · Horror, Thriller", movie.TextContent, StringComparison.Ordinal);
        Assert.Contains("Directed by John Carpenter", movie.TextContent, StringComparison.Ordinal);
        Assert.Contains(
            "Starring Donald Pleasence, Jamie Lee Curtis, Nancy Kyes, P. J. Soles, Charles Cyphers",
            movie.TextContent,
            StringComparison.Ordinal);
        Assert.Equal("data:image/jpeg;base64,/9j/4A==", movie.QuerySelector("img")?.GetAttribute("src"));
    }

    // The title box is ready for a box set's next movie, and the prompt says the label comes next.
    [Fact]
    public async Task Pick_MovieAdded_ClearsTheTitleForTheNextOne()
    {
        _intake.Search = Found(Halloween1978);
        _intake.Pick = Picked(Halloween);
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");

        await PickFirstAsync(cut);

        Assert.Equal(LabelPrompt, cut.Find(".intake-prompt").TextContent);
        Assert.Equal("", cut.Find("#intake-title").GetAttribute("value") ?? "");
    }

    // A second edition joins the movie the collection has (Overview: intake), and the list says so
    // for that movie alone: here a box set's first disc, beside a second that's new.
    [Fact]
    public async Task Pick_OwnedMovie_SaysTheCaseJoinsIt()
    {
        _intake.Search = Found(Halloween1978, Halloween2018);
        _intake.Pick = Picked(HalloweenOwned);
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");
        await PickFirstAsync(cut);
        _intake.Pick = Picked(HalloweenSequel);
        await SearchAsync(cut, "Halloween");

        await cut.FindAll(".tmdb-picker tbody tr")[1].KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(
            [true, false],
            cut.FindAll(".case-movie").Select(movie => movie.TextContent.Contains("Already in the collection; this case joins it", StringComparison.Ordinal)));
    }

    // ADR-0005: the case lists each movie once, and TMDB isn't asked for it again. The picker stays
    // open, for the clerk to pick the match they meant.
    [Fact]
    public async Task Pick_MovieAlreadyInTheCase_SaysSo()
    {
        _intake.Search = Found(Halloween1978, Halloween2018);
        _intake.Pick = Picked(Halloween);
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");
        await PickFirstAsync(cut);
        await SearchAsync(cut, "Halloween");

        await PickFirstAsync(cut);

        Assert.Equal("Halloween (1978) is already in this case.", cut.Find(".intake-prompt").TextContent);
        Assert.Single(cut.FindAll(".case-movie"));
        Assert.Single(cut.FindAll(".tmdb-picker"));
        Assert.Equal([948], _intake.Picks);
    }

    // The match the clerk meant goes in the case, and the prompt moves on to the label.
    [Fact]
    public async Task Pick_OtherMatchAfterOneAlreadyInTheCase_SaysTheLabelIsNext()
    {
        var cut = await RenderWithHalloweenPickedTwiceAsync();
        _intake.Pick = Picked(HalloweenSequel);

        await cut.FindAll(".tmdb-picker tbody tr")[1].KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(LabelPrompt, cut.Find(".intake-prompt").TextContent);
    }

    [Fact]
    public async Task KeyDownEscape_InThePickerAfterOneAlreadyInTheCase_SaysTheLabelIsNext()
    {
        var cut = await RenderWithHalloweenPickedTwiceAsync();

        await cut.Find(".tmdb-picker").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(LabelPrompt, cut.Find(".intake-prompt").TextContent);
    }

    // Build Plan 2.4's double feature: a title typed while the case holds a movie is the second
    // movie of the same case, so it joins the list and the barcode stays.
    [Fact]
    public async Task Pick_SecondTitleWhileCaseIsOpen_ListsTwoMovies()
    {
        var cut = await RenderWithTommyBoyPickedAsync();
        _intake.Search = Found(Halloween1978);
        _intake.Pick = Picked(Halloween);
        await SearchAsync(cut, "Halloween");

        await PickFirstAsync(cut);

        Assert.Equal(["Tommy Boy (1995)", "Halloween (1978)"], cut.FindAll(".case-movie-title").Select(title => title.TextContent));
        Assert.Equal("This DVD's barcode: 032429258373", cut.Find(".intake-barcode").TextContent);
    }

    // The picker stays, so the clerk can try again or pick another match.
    [Fact]
    public async Task Pick_TmdbDoesNotAnswer_KeepsThePickerOpen()
    {
        _intake.Search = Found(Halloween1978);
        _intake.Pick = Task.FromResult(new MoviePick(PickStatus.TryLater, null));
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
        Assert.Empty(_intake.Picks);
    }

    // UI-3: Esc waits for a pick being fetched, as Enter does. Closing the list would look like a
    // cancel, and then the movie would go in the case anyway.
    [Fact]
    public async Task KeyDownEscape_WhilePicking_KeepsThePickerOpen()
    {
        var (cut, picked, pick) = await StartPickingAsync();

        await cut.Find(".tmdb-picker").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Single(cut.FindAll(".tmdb-picker"));
        picked.SetResult(new MoviePick(PickStatus.Fetched, Halloween));
        await pick;
    }

    [Fact]
    public async Task ClickCancel_WhilePicking_KeepsThePickerOpen()
    {
        var (cut, picked, pick) = await StartPickingAsync();

        await cut.FindAll(".tmdb-picker button").Single(button => button.TextContent == "Cancel").ClickAsync(new MouseEventArgs());

        Assert.Single(cut.FindAll(".tmdb-picker"));
        picked.SetResult(new MoviePick(PickStatus.Fetched, Halloween));
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

        Assert.Equal("TMDB has no movie called \"Tommy Boy Bonus\". Fix the title, then search again, or enter it by hand.", cut.Find(".intake-prompt").TextContent);
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

    // The label scan saves the barcode on the case with its movies (ADR-0005), so it stays until then.
    [Fact]
    public async Task Pick_AfterAScan_KeepsTheBarcode()
    {
        _titles.Lookup = Task.FromResult(TommyBoyFound);
        _intake.Search = Found(TommyBoy1995);
        _intake.Pick = Picked(TommyBoy);
        var cut = Render<Intake>();
        await ScanAsync(cut, TommyBoyUpc);

        await PickFirstAsync(cut);

        Assert.Single(cut.FindAll(".case-movie"));
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

    // A title typed while the case holds a movie is the next movie in the same case, such as a
    // double feature's second (ADR-0005), so the case's barcode stays.
    [Fact]
    public async Task Submit_TitleTypedAfterAPick_KeepsTheBarcode()
    {
        var cut = await RenderWithTommyBoyPickedAsync();
        _intake.Search = Found(Halloween1978);

        await SearchAsync(cut, "Halloween");

        Assert.Equal("This DVD's barcode: 032429258373", cut.Find(".intake-barcode").TextContent);
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

    // Starting over loses the open case, though nothing of it was saved, so with a movie in it Esc
    // asks first. Cancel keeps the case as it was. The answer comes last, so Esc is awaited after it.
    [Fact]
    public async Task KeyDownEscape_WithMoviesInCase_CancelKeepsTheCase()
    {
        var cut = await RenderWithTommyBoyPickedAsync();
        var escape = cut.Find("#intake-title").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        await cut.InvokeAsync(() => _dialogs.Close(ok: false));
        await escape;

        Assert.Equal(["Tommy Boy (1995)"], cut.FindAll(".case-movie-title").Select(title => title.TextContent));
        Assert.Equal("This DVD's barcode: 032429258373", cut.Find(".intake-barcode").TextContent);
    }

    // OK starts over, back to how the screen opens, and nothing goes on the shelf.
    [Fact]
    public async Task KeyDownEscape_WithMoviesInCase_OkStartsOver()
    {
        var cut = await RenderWithTommyBoyPickedAsync();
        var escape = cut.Find("#intake-title").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal("Start over?\n\nNothing from this case has been saved.", _dialogs.Current?.Message);
        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        await escape;

        Assert.Empty(cut.FindAll(".case-contents"));
        Assert.Empty(cut.FindAll(".intake-barcode"));
        Assert.Equal("Scan the DVD's barcode, or type its title and press Enter.", cut.Find(".intake-prompt").TextContent);
        Assert.Empty(_intake.Shelvings);
    }

    // The error chord means something's wrong, and asking before starting over is routine: the
    // clerk pressed Esc on purpose, as Store Manager's Shut Down asks without it.
    [Fact]
    public async Task KeyDownEscape_WithMoviesInCase_PlaysNoErrorChord()
    {
        var cut = await RenderWithTommyBoyPickedAsync();
        var escape = cut.Find("#intake-title").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        await cut.InvokeAsync(() => _dialogs.Close(ok: false));
        await escape;

        Assert.DoesNotContain(Sound.Error, _sounds.Played);
    }

    // UI-3: not while TMDB is searching, as Esc doesn't close the list while a pick is being fetched:
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

    // One DVD at a time: a scan in the middle of a pick would start the next DVD before this one
    // is done.
    [Fact]
    public async Task Scanned_WhilePicking_IsIgnored()
    {
        var (cut, picked, pick) = await StartPickingAsync();

        await ScanAsync(cut, TommyBoyUpc);

        Assert.Empty(_titles.Lookups);
        Assert.Empty(cut.FindAll(".intake-barcode"));
        picked.SetResult(new MoviePick(PickStatus.Fetched, Halloween));
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

    // HW-5: member cards and concessions are Checkout's, and an unknown code means nothing anywhere.
    [Theory]
    [InlineData(ScanKind.MemberCard, "MEM-001")]
    [InlineData(ScanKind.Concession, "CON-POPCORN")]
    [InlineData(ScanKind.Unknown, "HELLO")]
    public async Task Scanned_AnythingButAUpcOrLabel_DoesNothing(ScanKind kind, string code)
    {
        var cut = Render<Intake>();

        _scanner.Emit(new Scan(kind, code));
        await cut.InvokeAsync(() => { });

        Assert.Empty(_titles.Lookups);
        Assert.Empty(cut.FindAll(".intake-barcode"));
        Assert.Equal("Scan the DVD's barcode, or type its title and press Enter.", cut.Find(".intake-prompt").TextContent);
    }

    // The label ends a case, so there's nothing for it to go on yet.
    [Fact]
    public async Task Scanned_LabelWithNoMovie_SaysToAddTheMovieFirst()
    {
        var cut = Render<Intake>();

        await ScanLabelAsync(cut, Label);

        Assert.Equal("Add the movie first, then scan the label.", cut.Find(".intake-prompt").TextContent);
        Assert.Empty(_intake.Shelvings);
    }

    // What the prompt said about the clerk's last action lasts only until their next one.
    [Fact]
    public async Task Scanned_UpcAfterAnEarlyLabel_SaysWhatItFound()
    {
        _titles.Lookup = Task.FromResult(TommyBoyFound);
        _intake.Search = Found(TommyBoy1995);
        var cut = Render<Intake>();
        await ScanLabelAsync(cut, Label);

        await ScanAsync(cut, TommyBoyUpc);

        Assert.Equal("Found Tommy Boy. Pick the movie, or press Esc to fix the title.", cut.Find(".intake-prompt").TextContent);
    }

    [Fact]
    public async Task Submit_TitleAfterAnEarlyLabel_SaysToPickTheMovie()
    {
        _intake.Search = Found(Halloween1978);
        var cut = Render<Intake>();
        await ScanLabelAsync(cut, Label);

        await SearchAsync(cut, "Halloween");

        Assert.Equal("Pick the movie by its poster and year, then press Enter.", cut.Find(".intake-prompt").TextContent);
    }

    // Once a case is on the shelf, it's empty, so Esc starts over without asking, and the prompt
    // stops saying what went on the shelf.
    [Fact]
    public async Task KeyDownEscape_AfterACaseIsShelved_SaysToScanTheNextDvd()
    {
        var cut = await RenderWithTommyBoyPickedAsync();
        _intake.Shelving = Shelved(Label, TommyBoy);
        await ScanLabelAsync(cut, Label);

        var escape = cut.Find("#intake-title").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Null(_dialogs.Current);
        await escape;
        Assert.Equal("Scan the DVD's barcode, or type its title and press Enter.", cut.Find(".intake-prompt").TextContent);
    }

    // A search for a box set's next movie found nothing, and the clerk scanned the label anyway. The
    // empty search goes with the case, so the next Esc starts over, emptying the title box, rather
    // than closing a list that isn't on screen.
    [Fact]
    public async Task KeyDownEscape_AfterNoMatches_StartsOver()
    {
        var cut = await RenderWithTommyBoyPickedAsync();
        _intake.Search = Found();
        await SearchAsync(cut, "Zzyzx");
        _intake.Shelving = Shelved(Label, TommyBoy);
        await ScanLabelAsync(cut, Label);

        await cut.Find("#intake-title").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal("", cut.Find("#intake-title").GetAttribute("value") ?? "");
        Assert.Equal("Scan the DVD's barcode, or type its title and press Enter.", cut.Find(".intake-prompt").TextContent);
    }

    // ADR-0005: the label scan saves the case as the screen lists it, with its barcode, and without
    // asking: the list is the confirmation.
    [Fact]
    public async Task Scanned_LabelAfterAPick_ShelvesTheCaseWithItsBarcode()
    {
        var cut = await RenderWithTommyBoyPickedAsync();
        _intake.Shelving = Shelved(Label, TommyBoy);

        await ScanLabelAsync(cut, Label);

        var (label, barcode, movies) = Assert.Single(_intake.Shelvings);
        Assert.Equal((Label, TommyBoyUpc), (label, barcode));
        Assert.Equal([TommyBoy], movies);
    }

    // UI-7: the register chimes, the prompt says what went on the shelf, and the screen is ready for
    // the next DVD.
    [Fact]
    public async Task Scanned_LabelShelved_ClearsTheCaseAndSaysSo()
    {
        _titles.Lookup = Task.FromResult(TommyBoyFound);
        _intake.Search = Found(TommyBoy1995);
        _intake.Pick = Picked(TommyBoy);
        _intake.Shelving = Shelved(Label, TommyBoy);
        var cut = Render<Intake>();
        var titleBox = TitleBoxId(cut);
        await ScanAsync(cut, TommyBoyUpc);
        await PickFirstAsync(cut);

        await ScanLabelAsync(cut, Label);

        Assert.Equal(
            "RV-00042 is on the shelf: Tommy Boy (1995). Scan the next DVD, or type its title and press Enter.",
            cut.Find(".intake-prompt").TextContent);
        Assert.Empty(cut.FindAll(".case-contents"));
        Assert.Empty(cut.FindAll(".intake-barcode"));
        Assert.Equal([Sound.Chime], _sounds.Played);
        AssertFocusedLast(titleBox, focuses: 5);
    }

    // A box set's line names every movie in its case.
    [Fact]
    public async Task Scanned_LabelShelvedForABoxSet_NamesEachMovie()
    {
        var cut = await RenderWithTommyBoyPickedAsync();
        _intake.Shelving = Shelved(Label, TommyBoy, Halloween);

        await ScanLabelAsync(cut, Label);

        Assert.StartsWith("RV-00042 is on the shelf: Tommy Boy (1995), Halloween (1978). ", cut.Find(".intake-prompt").TextContent, StringComparison.Ordinal);
    }

    // Build Plan 2.6: the case just shelved is one more on the shelf.
    [Fact]
    public async Task Scanned_LabelShelved_CountsOneMoreCase()
    {
        _intake.OnShelf = Task.FromResult(6);
        var cut = await RenderWithTommyBoyPickedAsync();
        _intake.Shelving = Shelved(Label, TommyBoy);

        await ScanLabelAsync(cut, Label);

        Assert.Equal("Cases on the shelf: 7", cut.Find(".intake-count").TextContent);
    }

    // A box set is one case on the shelf, however many movies it holds.
    [Fact]
    public async Task Scanned_LabelShelvedForABoxSet_CountsOneCase()
    {
        _intake.OnShelf = Task.FromResult(6);
        var cut = await RenderWithTommyBoyPickedAsync();
        _intake.Shelving = Shelved(Label, TommyBoy, Halloween);

        await ScanLabelAsync(cut, Label);

        Assert.Equal("Cases on the shelf: 7", cut.Find(".intake-count").TextContent);
    }

    // NET-12: a label another case has is the clerk's to fix. The error dialog says what to do, and
    // the case stays open, so the next label shelves it.
    [Fact]
    public async Task Scanned_LabelInUse_KeepsTheCaseOpen()
    {
        var cut = await RenderWithTommyBoyPickedAsync();
        _intake.Shelving = Task.FromResult(new Shelving(ShelveStatus.LabelInUse, null));
        await ScanLabelAsync(cut, Label);
        Assert.Equal("RV-00042 is already on another case.\n\nPeel the next label and scan that one.", _dialogs.Current?.Message);
        Assert.Equal(DialogIcon.Error, _dialogs.Current?.Icon);
        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        _intake.Shelving = Shelved(SecondLabel, TommyBoy);

        await ScanLabelAsync(cut, SecondLabel);

        Assert.Equal([Label, SecondLabel], _intake.Shelvings.Select(shelving => shelving.StoreLabel));
        Assert.Equal([TommyBoy], _intake.Shelvings[1].Movies);
        Assert.StartsWith("RV-00043 is on the shelf", cut.Find(".intake-prompt").TextContent, StringComparison.Ordinal);
    }

    // A label in use shelves nothing, so the count stays.
    [Fact]
    public async Task Scanned_LabelInUse_KeepsTheCount()
    {
        _intake.OnShelf = Task.FromResult(6);
        var cut = await RenderWithTommyBoyPickedAsync();
        _intake.Shelving = Task.FromResult(new Shelving(ShelveStatus.LabelInUse, null));
        await ScanLabelAsync(cut, Label);

        await cut.InvokeAsync(() => _dialogs.Close(ok: true));

        Assert.Equal("Cases on the shelf: 6", cut.Find(".intake-count").TextContent);
    }

    // OBS-5: the screen checks for a movie first, and its label and barcode are read as Core reads
    // them, so Core refusing the case for any of these is a bug. It goes to the screen's
    // ErrorBoundary, which shows the error dialog.
    [Theory]
    [InlineData(ShelveStatus.NoMovies)]
    [InlineData(ShelveStatus.NotALabel)]
    [InlineData(ShelveStatus.NotAUpc)]
    public async Task Scanned_LabelRefusedForWhatTheScreenChecks_Throws(ShelveStatus status)
    {
        var cut = await RenderWithTommyBoyPickedAsync();
        _intake.Shelving = Task.FromResult(new Shelving(status, null));

        await ScanLabelAsync(cut, Label);

        var thrown = await Renderer.UnhandledException.WaitAsync(TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken);
        Assert.IsType<UnreachableException>(thrown);
    }

    // A box set's next movie is still being picked, and the label would shelve the case without it.
    [Fact]
    public async Task Scanned_LabelWhilePickerIsOpen_SaysToFinishFirst()
    {
        var cut = await RenderWithTommyBoyPickedAsync();
        _intake.Search = Found(Halloween1978);
        await SearchAsync(cut, "Halloween");

        await ScanLabelAsync(cut, Label);

        Assert.Equal("Finish adding this movie, or press Esc, then scan the label.", cut.Find(".intake-prompt").TextContent);
        Assert.Empty(_intake.Shelvings);
    }

    [Fact]
    public async Task Scanned_LabelWhileEnteringByHand_SaysToFinishFirst()
    {
        var cut = await RenderWithTommyBoyPickedAsync();
        await OpenHandEntryAsync(cut);

        await ScanLabelAsync(cut, Label);

        Assert.Equal("Finish adding this movie, or press Esc, then scan the label.", cut.Find(".intake-prompt").TextContent);
        Assert.Empty(_intake.Shelvings);
    }

    // A factory UPC would start the next DVD and lose this case, which has no label yet. The case
    // stays as it was, and the warning says what to do.
    [Fact]
    public async Task Scanned_UpcWhileCaseHoldsAMovie_IsRefused()
    {
        var cut = await RenderWithTommyBoyPickedAsync();

        await ScanAsync(cut, HalloweenUpc);

        Assert.Equal(DialogIcon.Warning, _dialogs.Current?.Icon);
        Assert.Equal("This case doesn't have its store label yet.\n\nScan the label, or press Esc to start over.", _dialogs.Current?.Message);
        Assert.Equal([TommyBoyUpc], _titles.Lookups);
        Assert.Equal(["Tommy Boy (1995)"], cut.FindAll(".case-movie-title").Select(title => title.TextContent));
        Assert.Equal("This DVD's barcode: 032429258373", cut.Find(".intake-barcode").TextContent);
    }

    // One case at a time: a scan while the case is being saved would start the next one before this
    // one is on the shelf.
    [Fact]
    public async Task Scanned_WhileShelving_IsIgnored()
    {
        var cut = await RenderWithTommyBoyPickedAsync();
        var saved = new TaskCompletionSource<Shelving>(TaskCreationOptions.RunContinuationsAsynchronously);
        _intake.Shelving = saved.Task;
        await ScanLabelAsync(cut, Label);

        await ScanAsync(cut, HalloweenUpc);
        await ScanLabelAsync(cut, SecondLabel);

        Assert.Null(_dialogs.Current);
        Assert.Equal([TommyBoyUpc], _titles.Lookups);
        Assert.Equal([Label], _intake.Shelvings.Select(shelving => shelving.StoreLabel));
        saved.SetResult(new Shelving(ShelveStatus.Shelved, new ShelvedCase(Label, [new ShelvedMovie("Tommy Boy", 1995)])));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".case-contents")));
    }

    // TMDB doesn't have it: the form opens in the picker's place, with the title the clerk searched
    // for, to fix or keep.
    [Fact]
    public async Task EnterByHand_OpensWithTheTitlePrefilled()
    {
        _intake.Search = Found(Halloween1978);
        var cut = Render<Intake>();
        await SearchAsync(cut, "The Haddonfield Story");

        await OpenHandEntryAsync(cut);

        Assert.Equal("The Haddonfield Story", cut.Find("#hand-entry-title").GetAttribute("value"));
        Assert.Empty(cut.FindAll(".tmdb-picker"));
    }

    // UI-6: while the form is open, the prompt says what to type, whatever it said before.
    [Fact]
    public async Task EnterByHand_Opens_SaysWhatToType()
    {
        var cut = Render<Intake>();
        await ScanLabelAsync(cut, Label);

        await OpenHandEntryAsync(cut);

        Assert.Equal("Type what's on the case, then press Enter. Leave blank what you don't know.", cut.Find(".intake-prompt").TextContent);
    }

    // The movie Core takes goes in the case, as a pick does, and the prompt says the label comes next.
    [Fact]
    public async Task EnterByHand_Entered_ListsTheMovie()
    {
        _intake.HandEntry = new HandEntry(HandEntryStatus.Entered, new CaseMovieEntry.ByHand("The Haddonfield Story", 1985, 92, "PG"));
        var cut = Render<Intake>();
        await OpenHandEntryAsync(cut);

        await cut.Find(".hand-entry-form").SubmitAsync();

        Assert.Empty(cut.FindAll(".hand-entry"));
        var movie = cut.Find(".case-movie");
        Assert.Equal("The Haddonfield Story (1985)", movie.QuerySelector(".case-movie-title")?.TextContent);
        Assert.Contains("Rated PG · 92 minutes", movie.TextContent, StringComparison.Ordinal);
        Assert.Equal(LabelPrompt, cut.Find(".intake-prompt").TextContent);
    }

    // Core checks what was typed (NET-12), so the screen hands it over just as it was typed.
    [Fact]
    public async Task EnterByHand_Submit_SendsCoreWhatWasTyped()
    {
        var cut = Render<Intake>();
        await OpenHandEntryAsync(cut);
        await cut.Find("#hand-entry-title").InputAsync(" The Haddonfield Story ");
        await cut.Find("#hand-entry-year").InputAsync("1985 ");
        await cut.Find("#hand-entry-runtime").InputAsync("92");
        await cut.Find("#hand-entry-rating").ChangeAsync("PG");

        await cut.Find(".hand-entry-form").SubmitAsync();

        Assert.Equal([(" The Haddonfield Story ", "1985 ", "92", "PG")], _intake.HandEntries);
    }

    // NET-12: what Core refuses is the clerk's to fix, so the prompt line says how, not a dialog, and
    // the form stays open with what was typed.
    [Theory]
    [InlineData(HandEntryStatus.NoTitle, "Type the title.")]
    [InlineData(HandEntryStatus.NotAYear, "The year is four digits, such as 1978, or blank.")]
    [InlineData(HandEntryStatus.NotARuntime, "The runtime is in minutes, such as 91, or blank.")]
    [InlineData(HandEntryStatus.NotARating, "Pick a rating from the list, or Unknown.")]
    public async Task EnterByHand_Refused_SaysWhyInThePrompt(HandEntryStatus status, string prompt)
    {
        _intake.HandEntry = new HandEntry(status, null);
        var cut = Render<Intake>();
        await OpenHandEntryAsync(cut);

        await cut.Find(".hand-entry-form").SubmitAsync();

        Assert.Equal(prompt, cut.Find(".intake-prompt").TextContent);
        Assert.Single(cut.FindAll(".hand-entry"));
        Assert.Null(_dialogs.Current);
    }

    // Fixed and entered, the movie goes in the case, and the prompt stops saying what was wrong.
    [Fact]
    public async Task EnterByHand_EnteredAfterARefusal_SaysTheLabelIsNext()
    {
        _intake.HandEntry = new HandEntry(HandEntryStatus.NotAYear, null);
        var cut = Render<Intake>();
        await OpenHandEntryAsync(cut);
        await cut.Find(".hand-entry-form").SubmitAsync();
        _intake.HandEntry = new HandEntry(HandEntryStatus.Entered, new CaseMovieEntry.ByHand("The Haddonfield Story", 1985, null, null));

        await cut.Find(".hand-entry-form").SubmitAsync();

        Assert.Equal(LabelPrompt, cut.Find(".intake-prompt").TextContent);
    }

    [Fact]
    public async Task EnterByHand_EscapeAfterARefusal_StopsSayingWhy()
    {
        _intake.HandEntry = new HandEntry(HandEntryStatus.NotAYear, null);
        var cut = Render<Intake>();
        await OpenHandEntryAsync(cut);
        await cut.Find(".hand-entry-form").SubmitAsync();

        await cut.Find("#hand-entry-year").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal("Scan the DVD's barcode, or type its title and press Enter.", cut.Find(".intake-prompt").TextContent);
    }

    // UI-3: Esc cancels the form from any box, and the title box has the focus again, with the title
    // to search again.
    [Fact]
    public async Task EnterByHand_Escape_ClosesTheForm()
    {
        var cut = Render<Intake>();
        var titleBox = TitleBoxId(cut);
        await cut.Find("#intake-title").InputAsync("The Haddonfield Story");
        await OpenHandEntryAsync(cut);

        await cut.Find("#hand-entry-year").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".hand-entry"));
        Assert.Equal("The Haddonfield Story", cut.Find("#intake-title").GetAttribute("value"));
        AssertFocusedLast(titleBox, focuses: 3);
    }

    // UI-3: Esc closes the form from the title box too, as it closes the picker.
    [Fact]
    public async Task KeyDownEscape_InTheTitleBox_ClosesTheHandEntryForm()
    {
        var cut = Render<Intake>();
        await OpenHandEntryAsync(cut);

        await cut.Find("#intake-title").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".hand-entry"));
    }

    // A search from the title box opens the picker in the form's place.
    [Fact]
    public async Task Submit_WhileEnteringByHand_OpensThePickerInstead()
    {
        _intake.Search = Found(Halloween1978);
        var cut = Render<Intake>();
        await OpenHandEntryAsync(cut);

        await SearchAsync(cut, "Halloween");

        Assert.Empty(cut.FindAll(".hand-entry"));
        Assert.Single(cut.FindAll(".tmdb-picker"));
    }

    // With no movie in the case, a factory UPC starts over on the new DVD, as it does from the picker.
    [Fact]
    public async Task Scanned_UpcWhileEnteringByHand_StartsOverOnTheNewDvd()
    {
        var cut = Render<Intake>();
        await OpenHandEntryAsync(cut);

        await ScanAsync(cut, TommyBoyUpc);

        Assert.Empty(cut.FindAll(".hand-entry"));
        Assert.Equal([TommyBoyUpc], _titles.Lookups);
    }

    // UI-10: what the clerk typed is shown as text, never as markup.
    [Fact]
    public async Task EnterByHand_TitleWithMarkup_ShowsItAsText()
    {
        _intake.HandEntry = new HandEntry(HandEntryStatus.Entered, new CaseMovieEntry.ByHand("<b>Boo!</b>", null, null, null));
        var cut = Render<Intake>();
        await OpenHandEntryAsync(cut);

        await cut.Find(".hand-entry-form").SubmitAsync();

        Assert.Equal("<b>Boo!</b>", cut.Find(".case-movie-title").TextContent);
        Assert.Empty(cut.FindAll(".case-movie b"));
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

    // A count still being read when the clerk leaves stops too.
    [Fact]
    public async Task Dispose_WhileCounting_CancelsTheCount()
    {
        _intake.OnShelf = new TaskCompletionSource<int>().Task;
        _ = Render<Intake>();

        await DisposeComponentsAsync();

        Assert.True(_intake.LastCountToken.IsCancellationRequested);
    }

    // The count's cancellation raises nothing, though the screen has no catch for it: ComponentBase
    // ignores a task that was cancelled (Microsoft Learn, "ASP.NET Core Blazor synchronization
    // context"). This catches a Blazor that stops doing so, such as on a move to the next LTS (NET-13).
    [Fact]
    public async Task Dispose_WhileCounting_RaisesNothingOnceCancelled()
    {
        var counted = new TaskCompletionSource<int>();
        _intake.OnShelf = counted.Task;
        _ = Render<Intake>();
        await DisposeComponentsAsync();

        counted.SetCanceled(_intake.LastCountToken); // as EF Core ends a query whose token was cancelled
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the screen's own continuation

        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    private static Task<TmdbSearch> Found(params TmdbMatch[] matches) =>
        Task.FromResult(new TmdbSearch(TmdbStatus.Ok, matches));

    // A pick, as Core answers it: a movie the collection has, or one fetched from TMDB.
    private static Task<MoviePick> Picked(CaseMovieEntry movie) =>
        Task.FromResult(new MoviePick(movie is CaseMovieEntry.Owned ? PickStatus.Owned : PickStatus.Fetched, movie));

    // A label scan's save, as Core answers once the case is on the shelf.
    private static Task<Shelving> Shelved(string label, params CaseMovieEntry[] movies) =>
        Task.FromResult(new Shelving(
            ShelveStatus.Shelved,
            new ShelvedCase(label, [.. movies.Select(movie => new ShelvedMovie(movie.Title, movie.Year))])));

    // A factory UPC, as the scanner's thread would raise it, then a turn of the screen's own
    // thread, so the screen has handled it before the test looks.
    private async Task ScanAsync(IRenderedComponent<Intake> cut, string upc)
    {
        _scanner.Emit(new Scan(ScanKind.Upc, upc));
        await cut.InvokeAsync(() => { });
    }

    // A store label, as ScanAsync scans a factory UPC.
    private async Task ScanLabelAsync(IRenderedComponent<Intake> cut, string label)
    {
        _scanner.Emit(new Scan(ScanKind.StoreLabel, label));
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
        await cut.Find(".intake-search").SubmitAsync();
    }

    private static Task PickFirstAsync(IRenderedComponent<Intake> cut) =>
        cut.Find(".tmdb-picker tbody tr").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

    private static Task OpenHandEntryAsync(IRenderedComponent<Intake> cut) =>
        cut.FindAll(".intake-search button").Single(button => button.TextContent == "Can't find it? Enter by hand").ClickAsync(new MouseEventArgs());

    // The screen once Tommy Boy's barcode is scanned and the movie picked: the open case holds it,
    // with its barcode, waiting for its label.
    private async Task<IRenderedComponent<Intake>> RenderWithTommyBoyPickedAsync()
    {
        _titles.Lookup = Task.FromResult(TommyBoyFound);
        _intake.Search = Found(TommyBoy1995);
        _intake.Pick = Picked(TommyBoy);
        var cut = Render<Intake>();
        await ScanAsync(cut, TommyBoyUpc);
        await PickFirstAsync(cut);
        return cut;
    }

    // The screen once Halloween (1978) is in the case and the clerk has picked it again from a
    // second search: the prompt says it's in the case already, and the picker is still open.
    private async Task<IRenderedComponent<Intake>> RenderWithHalloweenPickedTwiceAsync()
    {
        _intake.Search = Found(Halloween1978, Halloween2018);
        _intake.Pick = Picked(Halloween);
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");
        await PickFirstAsync(cut);
        await SearchAsync(cut, "Halloween");
        await PickFirstAsync(cut);
        return cut;
    }

    // Searches, then picks Halloween (1978), whose pick waits until the test sets its answer.
    private async Task<(IRenderedComponent<Intake> Cut, TaskCompletionSource<MoviePick> Picked, Task Pick)> StartPickingAsync()
    {
        var picked = new TaskCompletionSource<MoviePick>(TaskCreationOptions.RunContinuationsAsynchronously);
        _intake.Search = Found(Halloween1978);
        _intake.Pick = picked.Task;
        var cut = Render<Intake>();
        await SearchAsync(cut, "Halloween");
        return (cut, picked, PickFirstAsync(cut));
    }
}
