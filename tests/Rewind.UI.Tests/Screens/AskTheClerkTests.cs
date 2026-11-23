using System.Diagnostics;
using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rewind.Core.Catalog;
using Rewind.Core.Devices;
using Rewind.Core.Scans;
using Rewind.Core.Shelves;
using Rewind.Core.Sounds;
using Rewind.UI.Dialogs;
using Rewind.UI.Errors;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class AskTheClerkTests : BunitContext
{
    private const string BeforeASearch = "Type part of a title or a name, type a year, or pick a genre, a rating or a shelf, then press Enter.";
    private const string NotAYear = "The year is four digits, such as 1978, or blank.";
    private const string NotARating = "Pick a rating from the list, or Any.";
    private const string TickTheShelves = "Tick or untick a shelf with Space, then press Enter to save, or Esc to cancel.";
    private const string LookForBox = "#ask-the-clerk-text";
    private const string YearBox = "#ask-the-clerk-year";
    private const string ShelfList = "#ask-the-clerk-shelf";

    private static readonly KeyboardEventArgs Escape = new() { Key = "Escape" };
    private static readonly KeyboardEventArgs Enter = new() { Key = "Enter" };

    // Jamie Lee Curtis's movies, as Core finds them by her name, some on the shelf and some checked out. Each has a
    // thumbnail of its own, so a list that kept another search's would show it.
    private static readonly MovieHit Halloween = new(1, "Halloween", 1978, "R", OnShelf: true, Thumb: [0xFF, 0xD8, 0xFF, 0xE0]);
    private static readonly MovieHit TradingPlaces = new(2, "Trading Places", 1983, "R", OnShelf: false, Thumb: [0xFF, 0xD8, 0xFF, 0xE1]);
    private static readonly MovieHit FreakyFriday = new(3, "Freaky Friday", 2003, "PG", OnShelf: true, Thumb: [0xFF, 0xD8, 0xFF, 0xE2]);
    private static readonly MovieHit TrueLies = new(4, "True Lies", 1994, "R", OnShelf: false, Thumb: [0xFF, 0xD8, 0xFF, 0xE3]);

    // Build Plan 4.2's movie, which goes on the Halloween and the Christmas shelves.
    private static readonly MovieHit Nightmare = new(5, "The Nightmare Before Christmas", 1993, "PG", OnShelf: true, Thumb: null);

    // Tonight's shelves, as Core lists them: a seasonal shelf in season, then each member's Staff Picks. No Id is in
    // that order, so a list that sorted them would show it.
    private static readonly ShelfRow HalloweenShelf = new(4, "Halloween");
    private static readonly ShelfRow DadsPicks = new(2, "Dad's Staff Picks");
    private static readonly ShelfRow MomsPicks = new(7, "Mom's Staff Picks");

    // Every shelf a movie can go on, as Core reads them for its shelves: Christmas, out of season tonight, then
    // Halloween, then each member's Staff Picks. The movie sits on Halloween and Dad's Staff Picks.
    private static readonly MovieShelfRow ChristmasRow = new(9, "Christmas", new Season(12, 1, 12, 25), HasTheMovie: false);
    private static readonly MovieShelfRow HalloweenRow = new(4, "Halloween", new Season(10, 1, 10, 31), HasTheMovie: true);
    private static readonly MovieShelfRow DadsPicksRow = new(2, "Dad's Staff Picks", Season: null, HasTheMovie: true);
    private static readonly MovieShelfRow MomsPicksRow = new(7, "Mom's Staff Picks", Season: null, HasTheMovie: false);
    private static readonly MovieShelfRow[] ItsShelves = [ChristmasRow, HalloweenRow, DadsPicksRow, MomsPicksRow];

    private readonly FakeCatalogSearch _catalog = new();
    private readonly FakeDisplayShelves _shelves = new();
    private readonly FakeScanner _scanner = new();
    private readonly FakeSoundPlayer _sounds = new();
    private readonly DialogService _dialogs;
    private readonly ListLogger<AskTheClerk> _log = new();
    private readonly ListLogger<ErrorHub> _errorLog = new();
    private bool _crashed;

    public AskTheClerkTests()
    {
        var errors = new ErrorHub(_errorLog);
        errors.Crashed += () => _crashed = true;
        _dialogs = new DialogService(_sounds);
        _catalog.Genres = Task.FromResult<IReadOnlyList<string>>(["Horror", "Comedy"]);
        _shelves.ListingTonights = Task.FromResult<IReadOnlyList<ShelfRow>>([HalloweenShelf, DadsPicks, MomsPicks]);
        _shelves.ListingMovieShelves = Task.FromResult<IReadOnlyList<MovieShelfRow>>(ItsShelves);
        Services.AddSingleton<ICatalogSearch>(_catalog);
        Services.AddSingleton<IDisplayShelves>(_shelves);
        Services.AddSingleton<IScanner>(_scanner);
        Services.AddSingleton(_dialogs);
        Services.AddSingleton<ILogger<AskTheClerk>>(_log);
        Services.AddSingleton(errors);
    }

    // UI-3: the clerk can type as soon as the screen opens.
    [Fact]
    public void Render_ScreenOpens_FocusesTheLookForBox()
    {
        var cut = Render<AskTheClerk>();

        AssertFocusedLast(ElementId(cut, LookForBox), focuses: 1);
    }

    // UI-6: before a search, the line says what can be typed and picked, and there's no list yet.
    [Fact]
    public void Render_ScreenOpens_SaysWhatCanBeTypedAndPicked()
    {
        var cut = Render<AskTheClerk>();

        Assert.Equal(BeforeASearch, Prompt(cut));
        Assert.Empty(cut.FindAll(".movie-hits-list"));
    }

    // Any is blank, which Core reads as any genre, then the genres in the order Core gave them.
    [Fact]
    public void Render_Genre_ListsAnyThenCoresGenres()
    {
        var cut = Render<AskTheClerk>();

        var options = cut.FindAll("#ask-the-clerk-genre option");

        Assert.Equal(["Any", "Horror", "Comedy"], options.Select(option => option.TextContent));
        Assert.Equal(["", "Horror", "Comedy"], options.Select(option => option.GetAttribute("value")));
    }

    // Any is blank, then the ratings Core takes.
    [Fact]
    public void Render_Rating_ListsAnyThenTheUsRatings()
    {
        var cut = Render<AskTheClerk>();

        var options = cut.FindAll("#ask-the-clerk-rating option");

        Assert.Equal(["Any", "G", "PG", "PG-13", "R", "NC-17"], options.Select(option => option.TextContent));
        Assert.Equal(["", "G", "PG", "PG-13", "R", "NC-17"], options.Select(option => option.GetAttribute("value")));
    }

    // UI-6: the loading state, as the screen opens, until the genres are in.
    [Fact]
    public async Task Render_GenresStillBeingRead_SaysSoUntilTheyAreIn()
    {
        var genres = new TaskCompletionSource<IReadOnlyList<string>>();
        _catalog.Genres = genres.Task;
        var cut = Render<AskTheClerk>();
        Assert.Equal("Reading the genres...", Prompt(cut));
        Assert.Equal(["Any"], cut.FindAll("#ask-the-clerk-genre option").Select(option => option.TextContent));

        await cut.InvokeAsync(() => genres.SetResult(["Horror", "Comedy"]));

        Assert.Equal(["Any", "Horror", "Comedy"], cut.FindAll("#ask-the-clerk-genre option").Select(option => option.TextContent));
        Assert.Equal(BeforeASearch, Prompt(cut));
    }

    // Once, as the screen opens: a search and Esc read them no more.
    [Fact]
    public async Task Render_ScreenOpens_ReadsTheGenresOnce()
    {
        var cut = Render<AskTheClerk>();
        await SearchForAsync(cut, "curtis");

        await cut.Find(LookForBox).KeyDownAsync(Escape);

        Assert.Equal(1, _catalog.GenreReads);
    }

    // Any is blank, which Core reads as every movie, on a shelf or not, then tonight's shelves in the order Core gave
    // them, each choice its shelf's Id.
    [Fact]
    public void Render_Shelf_ListsAnyThenTonightsShelves()
    {
        var cut = Render<AskTheClerk>();

        var options = cut.FindAll($"{ShelfList} option");

        Assert.Equal(["Any", "Halloween", "Dad's Staff Picks", "Mom's Staff Picks"], options.Select(option => option.TextContent));
        Assert.Equal(["", "4", "2", "7"], options.Select(option => option.GetAttribute("value")));
        Assert.Equal("Any", Chosen(cut.Find(ShelfList)));
    }

    // UI-6: the loading state, once the genres are in, until the shelves are too.
    [Fact]
    public async Task Render_ShelvesStillBeingRead_SaysSoUntilTheyAreIn()
    {
        var shelves = new TaskCompletionSource<IReadOnlyList<ShelfRow>>();
        _shelves.ListingTonights = shelves.Task;
        var cut = Render<AskTheClerk>();
        Assert.Equal("Reading the shelves...", Prompt(cut));
        Assert.Equal(["Any"], cut.FindAll($"{ShelfList} option").Select(option => option.TextContent));

        await cut.InvokeAsync(() => shelves.SetResult([DadsPicks]));

        Assert.Equal(["Any", "Dad's Staff Picks"], cut.FindAll($"{ShelfList} option").Select(option => option.TextContent));
        Assert.Equal(BeforeASearch, Prompt(cut));
    }

    // Once, as the screen opens: a search, Esc and a movie's shelves read them no more.
    [Fact]
    public async Task Render_ScreenOpens_ReadsTonightsShelvesOnce()
    {
        _catalog.Search = Found(Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await OpenShelvesAsync(cut);
        await FormButton(cut, "Cancel").ClickAsync(new MouseEventArgs());

        await cut.Find(LookForBox).KeyDownAsync(Escape);

        Assert.Equal(1, _shelves.TonightsReads);
    }

    // UI-10: a name typed on F5, a shelf's or a member's, renders as text, so markup in it shows as typed and draws
    // nothing.
    [Fact]
    public void Render_ShelfNamesWithMarkup_ShowThemAsTyped()
    {
        const string Typed = "<i>Halloween</i> & <b>Friends</b>";
        _shelves.ListingTonights = Task.FromResult<IReadOnlyList<ShelfRow>>([HalloweenShelf with { Name = Typed }]);

        var cut = Render<AskTheClerk>();

        Assert.Equal(["Any", Typed], cut.FindAll($"{ShelfList} option").Select(option => option.TextContent));
        Assert.Empty(cut.FindAll("i, b"));
    }

    // SEC-5: each box takes no more than Core reads. Core does the real checking.
    [Fact]
    public void Render_Boxes_CapHowMuchFits()
    {
        var cut = Render<AskTheClerk>();

        var caps = cut.FindAll("input").Select(box => (box.Id, box.GetAttribute("maxlength")));

        Assert.Equal([("ask-the-clerk-text", "200"), ("ask-the-clerk-year", "4")], caps);
    }

    // UI-3: Search is the default button, the form's one submit button, which Enter in a box or a list presses, drawn
    // with the dark border of a Windows 98 dialog's default.
    [Fact]
    public void Render_Buttons_SearchIsTheDefault()
    {
        var cut = Render<AskTheClerk>();

        var search = Assert.Single(cut.FindAll("button"));

        Assert.Equal(("Search", "submit"), (search.TextContent, search.GetAttribute("type")));
        Assert.Contains("default", search.ClassList);
    }

    // ARCH-5: one call to Core for each search, with each box in its own place, and the shelf as Core gave its Id. A
    // text box submits its form by itself, and keyboard.js has Enter in a list submit the list's own form, so each box
    // and list is in the one Search submits.
    [Theory]
    [InlineData(LookForBox)]
    [InlineData("#ask-the-clerk-genre")]
    [InlineData(YearBox)]
    [InlineData("#ask-the-clerk-rating")]
    [InlineData(ShelfList)]
    public async Task Submit_EnterInEachBoxOrList_SearchesOnceWithEachBoxInItsPlace(string control)
    {
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, "curtis", "Horror", "1978", "R", DadsPicks);
        var form = cut.Find(control).Closest("form");
        Assert.NotNull(form);

        await form.SubmitAsync();

        Assert.Equal([("curtis", "Horror", "1978", "R", 2)], _catalog.Searches);
    }

    [Fact]
    public async Task ClickSearch_BoxesFilledIn_SearchesOnceWithEachBoxInItsPlace()
    {
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, "curtis", "Horror", "1978", "R", MomsPicks);

        await cut.Find("button").ClickAsync(new MouseEventArgs());

        Assert.Equal([("curtis", "Horror", "1978", "R", 7)], _catalog.Searches);
    }

    // Any is blank, and Core reads a blank box as nothing picked, so nothing typed or picked lists the whole catalog.
    [Fact]
    public async Task Submit_NothingTypedOrPicked_SendsEachBoxBlank()
    {
        var cut = Render<AskTheClerk>();

        await SubmitAsync(cut);

        Assert.Equal([("", "", "", "", null)], _catalog.Searches);
    }

    // SEC-5: the screen hands Core what was typed, spaces and all, and Core cleans it.
    [Fact]
    public async Task Submit_SpacesAroundWhatWasTyped_SendsItAsTyped()
    {
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, " Jamie Lee Curtis ", year: "1978 ");

        await SubmitAsync(cut);

        Assert.Equal([(" Jamie Lee Curtis ", "", "1978 ", "", null)], _catalog.Searches);
    }

    // UI-6: the loading state.
    [Fact]
    public void Submit_CoreStillSearching_SaysSo()
    {
        _catalog.Search = new TaskCompletionSource<SearchResult>().Task;
        var cut = Render<AskTheClerk>();

        _ = SubmitAsync(cut);

        Assert.Equal("Searching...", Prompt(cut));
    }

    // Each Enter submits again, and the first search's answer is on its way.
    [Fact]
    public async Task Submit_TwiceWhileSearching_SearchesOnce()
    {
        var searched = new TaskCompletionSource<SearchResult>();
        _catalog.Search = searched.Task;
        var cut = Render<AskTheClerk>();
        var first = SubmitAsync(cut);

        var second = SubmitAsync(cut);

        await cut.InvokeAsync(() => searched.SetResult(new SearchResult(SearchStatus.Searched, [Halloween], MoreMatched: false)));
        await first;
        await second;
        Assert.Single(_catalog.Searches);
    }

    // Build Plan 4.1's Done when, as the clerk sees it: an actor's name lists their movies, and each says whether a
    // case of it is on the shelf. Core's own tests pin the search itself.
    [Fact]
    public async Task Submit_DoneWhen_AnActorsNameListsTheirMoviesWithTheOnesOnTheShelfMarked()
    {
        _catalog.Search = Found(Halloween, TradingPlaces);
        var cut = Render<AskTheClerk>();

        await SearchForAsync(cut, "Jamie Lee Curtis");

        Assert.Equal("Jamie Lee Curtis", Assert.Single(_catalog.Searches).Text);
        Assert.Equal(
            [("Halloween", "On the shelf"), ("Trading Places", "Checked out")],
            Rows(cut).Select(row => (Title(row), row.Children[3].TextContent)));
    }

    // UI-3: the arrow keys work in the list at once.
    [Fact]
    public async Task Submit_Found_FocusesTheListsFirstRow()
    {
        _catalog.Search = Found(Halloween, TradingPlaces);
        var cut = Render<AskTheClerk>();

        await SubmitAsync(cut);

        AssertFocusedLast(ElementId(cut, ".movie-hits-list tbody tr"), focuses: 2);
    }

    // The list sets itself up once, as it opens, so each search has a list of its own: its own rows, with its own
    // thumbnails, and its first row focused.
    [Fact]
    public async Task Submit_SecondSearch_ShowsItsOwnRowsAndThumbsWithItsFirstRowFocused()
    {
        _catalog.Search = Found(Halloween, TradingPlaces);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        _catalog.Search = Found(FreakyFriday, TrueLies);

        await SubmitAsync(cut);

        var firstRow = ElementId(cut, ".movie-hits-list tbody tr");
        Assert.Equal(["Freaky Friday", "True Lies"], Rows(cut).Select(Title));
        Assert.Equal(
            ["data:image/jpeg;base64,/9j/4g==", "data:image/jpeg;base64,/9j/4w=="],
            cut.FindAll(".movie-hits-list img").Select(thumb => thumb.GetAttribute("src")));
        AssertFocusedLast(firstRow, focuses: 3);
    }

    // UI-6: how many came back, and how many of those are on the shelf.
    [Theory]
    [InlineData("3 movies. 2 are on the shelf.", true, false, true)]
    [InlineData("1 movie. 1 is on the shelf.", true)]
    [InlineData("2 movies. 0 are on the shelf.", false, false)]
    public async Task Submit_Found_SaysHowManyAndHowManyAreOnTheShelf(string line, params bool[] onShelf)
    {
        _catalog.Search = Found([.. onShelf.Select((on, i) => new MovieHit(i + 1, $"Movie {i + 1}", 1978, "R", on, Thumb: null))]);
        var cut = Render<AskTheClerk>();

        await SubmitAsync(cut);

        Assert.Equal(line, Prompt(cut));
    }

    // Core holds the first of them when more matched, and the number is how many came back.
    [Fact]
    public async Task Submit_MoreMatched_SaysTheseAreTheFirstAndToTypeMore()
    {
        _catalog.Search = Task.FromResult(new SearchResult(SearchStatus.Searched, [Halloween, TradingPlaces, FreakyFriday], MoreMatched: true));
        var cut = Render<AskTheClerk>();

        await SubmitAsync(cut);

        Assert.Equal("The first 3 movies. 2 are on the shelf. Type more to narrow it down.", Prompt(cut));
    }

    // UI-6: the empty state, for a search that asked for something.
    [Fact]
    public async Task Submit_NothingMatches_SaysToCheckTheSpelling()
    {
        var cut = Render<AskTheClerk>();

        await SearchForAsync(cut, "hallowen");

        Assert.Equal("Nothing in the store matches. Check the spelling, then press Enter.", Prompt(cut));
        Assert.Empty(cut.FindAll(".movie-hits-list"));
    }

    // Any one box asked for something, so finding nothing is about what was asked, not the store.
    [Theory]
    [InlineData("curtis", "", "", "")]
    [InlineData("", "Horror", "", "")]
    [InlineData("", "", "1978", "")]
    [InlineData("", "", "", "R")]
    public async Task Submit_OneBoxFilledInAndNothingFound_SaysToCheckTheSpelling(string text, string genre, string year, string rating)
    {
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, text, genre, year, rating);

        await SubmitAsync(cut);

        Assert.Equal("Nothing in the store matches. Check the spelling, then press Enter.", Prompt(cut));
    }

    // Nothing typed or picked asks for the whole catalog, so finding nothing means the store has nothing listed.
    [Fact]
    public async Task Submit_NothingAskedAndNothingFound_SaysTheStoreHasNoMoviesYet()
    {
        var cut = Render<AskTheClerk>();

        await SubmitAsync(cut);

        Assert.Equal("The store has no movies yet. Press F4 to add one.", Prompt(cut));
    }

    // Core reads a box of only spaces as blank, so that's nothing asked too.
    [Fact]
    public async Task Submit_OnlySpacesTypedAndNothingFound_SaysTheStoreHasNoMoviesYet()
    {
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, "   ", year: " ");

        await SubmitAsync(cut);

        Assert.Equal("The store has no movies yet. Press F4 to add one.", Prompt(cut));
    }

    // UI-6: a shelf alone asks for the movies on it, so finding nothing means nothing is on it yet, and the line says
    // how to put a movie there: Esc clears the search, shelf and all, for one that finds the movie.
    [Fact]
    public async Task Submit_OnlyAShelfPickedAndNothingFound_SaysNothingIsOnItYetAndHowToPutAMovieThere()
    {
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, "  ", shelf: DadsPicks);

        await SubmitAsync(cut);

        Assert.Equal(
            "Nothing is on Dad's Staff Picks yet. To put a movie there, press Esc, search for it, then press Enter on it.",
            Prompt(cut));
        Assert.Equal(("  ", "", "", "", 2), Assert.Single(_catalog.Searches));
    }

    // A shelf and any one box asked for something more, so finding nothing is about what was asked, as without a
    // shelf.
    [Theory]
    [InlineData("curtis", "", "", "")]
    [InlineData("", "Horror", "", "")]
    [InlineData("", "", "1978", "")]
    [InlineData("", "", "", "R")]
    public async Task Submit_AShelfAndAnotherBoxAndNothingFound_SaysToCheckTheSpelling(string text, string genre, string year, string rating)
    {
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, text, genre, year, rating, HalloweenShelf);

        await SubmitAsync(cut);

        Assert.Equal("Nothing in the store matches. Check the spelling, then press Enter.", Prompt(cut));
    }

    // The line is about the search that ran, so a shelf picked after it changes nothing until Enter.
    [Fact]
    public async Task Change_ShelfAfterASearchOfOneAlone_TheLineStillNamesTheOneSearched()
    {
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, shelf: HalloweenShelf);
        await SubmitAsync(cut);

        await PickShelfAsync(cut, MomsPicks);

        Assert.Equal(
            "Nothing is on Halloween yet. To put a movie there, press Esc, search for it, then press Enter on it.",
            Prompt(cut));
    }

    // A shelf with movies on it is a search like any other.
    [Fact]
    public async Task Submit_OnlyAShelfPickedAndFound_SaysHowManyAndHowManyAreOnTheShelf()
    {
        _catalog.Search = Found(Halloween, TradingPlaces);
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, shelf: HalloweenShelf);

        await SubmitAsync(cut);

        Assert.Equal("2 movies. 1 is on the shelf.", Prompt(cut));
        Assert.Equal(["Halloween", "Trading Places"], Rows(cut).Select(Title));
    }

    // NET-12: a year that isn't one is the clerk's to fix, so the line says how, in Intake's words, not a dialog, and
    // the cursor goes back to the Year box.
    [Fact]
    public async Task Submit_NotAYear_SaysTheYearIsFourDigitsAndPutsTheCursorInTheYearBox()
    {
        _catalog.Search = DidNotRun(SearchStatus.NotAYear);
        var cut = Render<AskTheClerk>();
        var yearBox = ElementId(cut, YearBox);
        await FillInAsync(cut, year: "78");

        await SubmitAsync(cut);

        Assert.Equal(NotAYear, Prompt(cut));
        AssertFocusedLast(yearBox, focuses: 2);
        Assert.Null(_dialogs.Current);
    }

    // The list can't send a rating that isn't one, but Core still says so if it did, as Intake's form does.
    [Fact]
    public async Task Submit_NotARating_SaysToPickOneFromTheList()
    {
        _catalog.Search = DidNotRun(SearchStatus.NotARating);
        var cut = Render<AskTheClerk>();

        await SubmitAsync(cut);

        Assert.Equal(NotARating, Prompt(cut));
        Assert.Null(_dialogs.Current);
    }

    // A search that didn't run found nothing, so the last search's hits go with it.
    [Theory]
    [InlineData(SearchStatus.NotAYear)]
    [InlineData(SearchStatus.NotARating)]
    public async Task Submit_DidNotRun_ClearsTheHitsItHad(SearchStatus status)
    {
        _catalog.Search = Found(Halloween);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        _catalog.Search = DidNotRun(status);

        await SubmitAsync(cut);

        Assert.Empty(cut.FindAll(".movie-hits-list"));
    }

    // UI-6: what's happening comes first, even over what was wrong with the last search.
    [Fact]
    public async Task Submit_AfterARefusal_SaysItsSearching()
    {
        _catalog.Search = DidNotRun(SearchStatus.NotAYear);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        _catalog.Search = new TaskCompletionSource<SearchResult>().Task;

        _ = SubmitAsync(cut);

        Assert.Equal("Searching...", Prompt(cut));
    }

    // Fixed and searched, the line stops saying what was wrong.
    [Fact]
    public async Task Submit_RanAfterARefusal_SaysWhatItFound()
    {
        _catalog.Search = DidNotRun(SearchStatus.NotAYear);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        _catalog.Search = Found(Halloween);

        await SubmitAsync(cut);

        Assert.Equal("1 movie. 1 is on the shelf.", Prompt(cut));
    }

    // OBS-5: a status the screen doesn't know, such as one Core gained later, is a bug, so it goes to the screen's
    // ErrorBoundary rather than doing nothing.
    [Fact]
    public async Task Submit_SearchAnswersAStatusTheScreenDoesNotKnow_Throws()
    {
        _catalog.Search = DidNotRun((SearchStatus)99);
        var cut = Render<AskTheClerk>();

        await Assert.ThrowsAsync<UnreachableException>(() => SubmitAsync(cut));
    }

    // While the screen is open, a failure goes to its ErrorBoundary, not to ErrorHub.
    [Fact]
    public async Task Submit_SearchFails_HandsItToTheErrorBoundary()
    {
        _catalog.Search = Task.FromException<SearchResult>(new InvalidOperationException("The search failed."));
        var cut = Render<AskTheClerk>();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => SubmitAsync(cut));

        Assert.Equal("The search failed.", thrown.Message);
        Assert.False(_crashed);
    }

    // The same for the genres' read, as the screen opens.
    [Fact]
    public async Task Render_GenresReadFails_HandsItToTheErrorBoundary()
    {
        var genres = new TaskCompletionSource<IReadOnlyList<string>>();
        _catalog.Genres = genres.Task;
        _ = Render<AskTheClerk>();

        await Renderer.Dispatcher.InvokeAsync(() => genres.SetException(new InvalidOperationException("The read failed.")));

        Assert.True(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
    }

    // UI-3: Esc in the search clears it, from any box, list or button: the text, the three lists back on Any, and the
    // hits.
    [Theory]
    [InlineData(LookForBox)]
    [InlineData("#ask-the-clerk-genre")]
    [InlineData(YearBox)]
    [InlineData("#ask-the-clerk-rating")]
    [InlineData(ShelfList)]
    [InlineData("button")]
    public async Task KeyDownEscape_InTheSearch_ClearsTheBoxesThePicksAndTheHits(string control)
    {
        _catalog.Search = Found(Halloween, TradingPlaces);
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, "curtis", "Horror", "1978", "R", DadsPicks);
        await SubmitAsync(cut);

        await cut.Find(control).KeyDownAsync(Escape);

        Assert.Equal(("", "", "", "", ""), Boxes(cut));
        Assert.Equal(["Any", "Any", "Any"], cut.FindAll("select").Select(Chosen));
        Assert.Empty(cut.FindAll(".movie-hits-list"));
        Assert.Equal(BeforeASearch, Prompt(cut));
    }

    // The clerk can type the next search at once.
    [Fact]
    public async Task KeyDownEscape_InTheSearch_PutsTheCursorInTheLookForBox()
    {
        var cut = Render<AskTheClerk>();
        var lookFor = ElementId(cut, LookForBox);

        await cut.Find(YearBox).KeyDownAsync(Escape);

        AssertFocusedLast(lookFor, focuses: 2);
    }

    [Fact]
    public async Task KeyDownEscape_AfterARefusal_StopsSayingWhy()
    {
        _catalog.Search = DidNotRun(SearchStatus.NotAYear);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);

        await cut.Find(YearBox).KeyDownAsync(Escape);

        Assert.Equal(BeforeASearch, Prompt(cut));
    }

    // Not while Core answers: the answer would land on a search that had been cleared. It shows what it found for
    // what's still in the boxes.
    [Fact]
    public async Task KeyDownEscape_WhileSearching_ClearsNothing()
    {
        var searched = new TaskCompletionSource<SearchResult>();
        _catalog.Search = searched.Task;
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, "curtis", "Horror", "1978", "R", DadsPicks);
        var search = SubmitAsync(cut);

        await cut.Find(LookForBox).KeyDownAsync(Escape);

        await cut.InvokeAsync(() => searched.SetResult(new SearchResult(SearchStatus.Searched, [Halloween], MoreMatched: false)));
        await search;
        Assert.Equal(("curtis", "Horror", "1978", "R", "2"), Boxes(cut));
        Assert.Equal(["Halloween"], Rows(cut).Select(Title));
    }

    [Fact]
    public async Task KeyDown_AnyOtherKeyInTheSearch_ClearsNothing()
    {
        _catalog.Search = Found(Halloween);
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, "curtis", "Horror", "1978", "R", DadsPicks);
        await SubmitAsync(cut);

        await cut.Find(LookForBox).KeyDownAsync(new KeyboardEventArgs { Key = "a" });

        Assert.Equal(("curtis", "Horror", "1978", "R", "2"), Boxes(cut));
        Assert.Equal(["Halloween"], Rows(cut).Select(Title));
    }

    // UI-3: Esc on the list puts the cursor back in the Look for box. The box keeps what was typed, with the cursor
    // after it, so typing adds to it, and a second Esc starts over.
    [Fact]
    public async Task KeyDownEscape_OnTheList_PutsTheCursorInTheLookForBox()
    {
        _catalog.Search = Found(Halloween, TradingPlaces);
        var cut = Render<AskTheClerk>();
        var lookFor = ElementId(cut, LookForBox);
        await SubmitAsync(cut);

        await Rows(cut)[1].KeyDownAsync(Escape);

        AssertFocusedLast(lookFor, focuses: 3);
    }

    // The cursor moves once: the screen redraws as the clerk types or picks, and a redraw never pulls the cursor back
    // to the box from the list the clerk picked in.
    [Fact]
    public async Task Change_PickAfterEscOnTheList_LeavesTheCursorWhereTheClerkPutIt()
    {
        _catalog.Search = Found(Halloween, TradingPlaces);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await Rows(cut)[1].KeyDownAsync(Escape);

        await cut.Find("#ask-the-clerk-genre").ChangeAsync(new ChangeEventArgs { Value = "Horror" });

        JSInterop.VerifyFocusAsyncInvoke(3); // the box as the screen opened, the list's first row, then the box again
    }

    // And goes no further, as Which movie?'s Esc stops at its picker: the list sits outside the search, so the
    // search's own Esc, which would clear it, never hears it.
    [Fact]
    public async Task KeyDownEscape_OnTheList_LeavesTheSearchAndItsHits()
    {
        _catalog.Search = Found(Halloween, TradingPlaces);
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, "curtis", "Horror", "1978", "R", DadsPicks);
        await SubmitAsync(cut);

        await Rows(cut)[1].KeyDownAsync(Escape);

        Assert.Equal(("curtis", "Horror", "1978", "R", "2"), Boxes(cut));
        Assert.Equal(["Halloween", "Trading Places"], Rows(cut).Select(Title));
        Assert.Equal("2 movies. 1 is on the shelf.", Prompt(cut));
    }

    // Ask the Clerk takes no scans, so each kind says so, and where it goes.
    [Theory]
    [InlineData(ScanKind.StoreLabel, "RV-00042")]
    [InlineData(ScanKind.MemberCard, "MEM-001")]
    [InlineData(ScanKind.Concession, "CON-POPCORN")]
    [InlineData(ScanKind.Upc, "032429258373")]
    public async Task Scanned_AnyStoreBarcode_SaysWhichScreenTakesIt(ScanKind kind, string code)
    {
        var cut = Render<AskTheClerk>();

        await ScanAsync(cut, kind, code);

        var dialog = Assert.IsType<Dialog>(_dialogs.Current);
        Assert.Equal(
            ("Ask the Clerk doesn't take scans.\n\nPress F1 to check out, F2 to return a movie or F4 to add one, then scan again.", DialogIcon.Warning),
            (dialog.Message, dialog.Icon));
        Assert.Empty(_catalog.Searches);
    }

    // HW-5: ScannerHub logged it and played the chord, so the screen has nothing to add.
    [Fact]
    public async Task Scanned_UnknownBarcode_DoesNothing()
    {
        var cut = Render<AskTheClerk>();

        await ScanAsync(cut, ScanKind.Unknown, "WHAT-IS-THIS");

        Assert.Null(_dialogs.Current);
        Assert.Empty(_log.Entries);
    }

    // One thing at a time, as on Return.
    [Fact]
    public async Task Scanned_WhileSearching_IsIgnoredAndLogged()
    {
        _catalog.Search = new TaskCompletionSource<SearchResult>().Task;
        var cut = Render<AskTheClerk>();
        _ = SubmitAsync(cut);

        await ScanAsync(cut, ScanKind.StoreLabel, "RV-00042");

        Assert.Null(_dialogs.Current);
        AssertLoggedIgnored("Ignored the StoreLabel RV-00042: Ask the Clerk is still busy or a dialog is open");
    }

    // UI-5: as in Windows 98, an open dialog is answered first. A second dialog would replace it, with a second chord.
    [Fact]
    public async Task Scanned_WhileADialogIsOpen_IsIgnoredAndLogged()
    {
        var cut = Render<AskTheClerk>();
        await ScanAsync(cut, ScanKind.MemberCard, "MEM-001");

        await ScanAsync(cut, ScanKind.StoreLabel, "RV-00042");

        Assert.Equal([Sound.Error], _sounds.Played);
        AssertLoggedIgnored("Ignored the StoreLabel RV-00042: Ask the Clerk is still busy or a dialog is open");
    }

    // The scanner outlives the screen, so the screen stops listening when the clerk leaves it.
    [Fact]
    public async Task Dispose_ClerkLeavesTheScreen_StopsListeningForScans()
    {
        _ = Render<AskTheClerk>();

        await DisposeComponentsAsync();

        Assert.False(_scanner.IsListenedTo);
    }

    // A scan the scanner's thread raised just before the clerk left reaches the screen afterwards. The screen has
    // closed, so no dialog opens over the next one, and nothing is raised.
    [Fact]
    public async Task Dispose_ScanRaisedJustBefore_RaisesNothing()
    {
        _ = Render<AskTheClerk>();
        var raisedJustBefore = _scanner.RaisedBeforeStopping();
        await DisposeComponentsAsync();

        raisedJustBefore(new Scan(ScanKind.StoreLabel, "RV-00042"));
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind anything the scan started

        Assert.Null(_dialogs.Current);
        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
    }

    [Fact]
    public async Task Dispose_WhileReadingTheGenres_CancelsTheRead()
    {
        _catalog.Genres = new TaskCompletionSource<IReadOnlyList<string>>().Task;
        _ = Render<AskTheClerk>();

        await DisposeComponentsAsync();

        Assert.True(_catalog.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Dispose_WhileSearching_CancelsTheSearch()
    {
        _catalog.Search = new TaskCompletionSource<SearchResult>().Task;
        var cut = Render<AskTheClerk>();
        _ = SubmitAsync(cut);

        await DisposeComponentsAsync();

        Assert.True(_catalog.LastToken.IsCancellationRequested);
    }

    // ADR-0011: an answer that arrives after the clerk left, rather than being cancelled, draws nothing and moves no
    // cursor, since the screen has gone: hits, or a year that isn't one, whose cursor would go to the Year box.
    [Theory]
    [InlineData(SearchStatus.Searched)]
    [InlineData(SearchStatus.NotAYear)]
    public async Task Dispose_SearchAnswersAfterTheClerkLeft_ShowsNothing(SearchStatus status)
    {
        var searched = new TaskCompletionSource<SearchResult>();
        _catalog.Search = searched.Task;
        var cut = Render<AskTheClerk>();
        var search = SubmitAsync(cut);
        await DisposeComponentsAsync();

        searched.SetResult(new SearchResult(status, status == SearchStatus.Searched ? [Halloween, TradingPlaces] : [], MoreMatched: false));
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the screen's own continuation
        await search;

        JSInterop.VerifyFocusAsyncInvoke(); // the Look for box's, as the screen opened, and no other
        Assert.Null(_dialogs.Current);
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // The cancellation leaving causes goes nowhere: WhileBusyAsync catches it.
    [Fact]
    public async Task Dispose_WhileSearching_RaisesNothingOnceCancelled()
    {
        var searched = new TaskCompletionSource<SearchResult>();
        _catalog.Search = searched.Task;
        var cut = Render<AskTheClerk>();
        var search = SubmitAsync(cut);
        await DisposeComponentsAsync();

        searched.SetCanceled(_catalog.LastToken); // as EF Core ends a query whose token was cancelled
        await search;

        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
    }

    // #83's route: a search that fails after the clerk left has no screen to show it on, so it goes to ErrorHub, and
    // it's the exception the search threw, for the log to keep its details (OBS-4).
    [Fact]
    public async Task Dispose_WhileSearching_ReportsAFailureToErrorHub()
    {
        var searched = new TaskCompletionSource<SearchResult>();
        _catalog.Search = searched.Task;
        var cut = Render<AskTheClerk>();
        var search = SubmitAsync(cut);
        await DisposeComponentsAsync();
        var failure = new InvalidOperationException("The search failed.");

        searched.SetException(failure);
        await search;

        Assert.True(_crashed);
        Assert.Equal([(LogLevel.Error, "Unhandled exception from AskTheClerk; reported to the shell", (Exception?)failure)], _errorLog.Entries);
    }

    // The genres wait in the busy line too, so a read that fails after the clerk left takes the same route.
    [Fact]
    public async Task Dispose_WhileReadingTheGenres_ReportsAFailureToErrorHub()
    {
        var genres = new TaskCompletionSource<IReadOnlyList<string>>();
        _catalog.Genres = genres.Task;
        _ = Render<AskTheClerk>();
        await DisposeComponentsAsync();
        var failure = new InvalidOperationException("The read failed.");

        await Renderer.Dispatcher.InvokeAsync(() => genres.SetException(failure));

        Assert.True(_crashed);
        Assert.Equal([(LogLevel.Error, "Unhandled exception from AskTheClerk; reported to the shell", (Exception?)failure)], _errorLog.Entries);
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // ARCH-5: Enter or a double-click on a movie asks Core once for every shelf it can go on, then opens them in the
    // list's place, the form named for the movie, with the shelves it sits on ticked as Core read them.
    [Theory]
    [InlineData("Enter")]
    [InlineData("a double-click")]
    public async Task Pick_EnterOrADoubleClickOnAMovie_ReadsItsShelvesOnceThenOpensThem(string way)
    {
        _catalog.Search = Found(Halloween, Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);

        await (way == "Enter" ? Rows(cut)[1].KeyDownAsync(Enter) : Rows(cut)[1].DoubleClickAsync(new MouseEventArgs()));

        Assert.Equal(["ListMovieShelves 5"], _shelves.Calls);
        Assert.Equal("Shelves for The Nightmare Before Christmas (1993)", Form(cut).QuerySelector("legend")?.TextContent);
        Assert.Equal([false, true, true, false], Checkboxes(cut).Select(checkbox => checkbox.HasAttribute("checked")));
    }

    // While the shelves are open, the list stays in the page out of sight, the same list with its rows, and the search
    // ignores the mouse and Tab, so Shift+Tab from the first checkbox can't land on Search.
    [Fact]
    public async Task Pick_ShelvesOpen_PutTheListOutOfSightAndTheSearchOutOfReach()
    {
        _catalog.Search = Found(Halloween, Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        var list = cut.FindComponent<MovieHits>().Instance;

        await OpenShelvesAsync(cut, row: 1);

        Assert.True(cut.Find(".movie-hits-list").HasAttribute("hidden"));
        Assert.Same(list, cut.FindComponent<MovieHits>().Instance);
        Assert.Equal(["Halloween", "The Nightmare Before Christmas"], Rows(cut).Select(Title));
        Assert.True(cut.Find(".ask-the-clerk-search").HasAttribute("inert"));
    }

    // Before a pick, the list is in sight and the search in reach.
    [Fact]
    public async Task Submit_Found_ShowsTheListWithTheSearchInReach()
    {
        _catalog.Search = Found(Halloween, Nightmare);
        var cut = Render<AskTheClerk>();

        await SubmitAsync(cut);

        Assert.False(cut.Find(".movie-hits-list").HasAttribute("hidden"));
        Assert.False(cut.Find(".ask-the-clerk-search").HasAttribute("inert"));
        Assert.Empty(cut.FindAll(".movie-shelves"));
    }

    // UI-6: the loading state, until Core has read the movie's shelves.
    [Fact]
    public async Task Pick_CoreStillReading_SaysSo()
    {
        _catalog.Search = Found(Nightmare);
        _shelves.ListingMovieShelves = new TaskCompletionSource<IReadOnlyList<MovieShelfRow>>().Task;
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);

        _ = OpenShelvesAsync(cut);

        Assert.Equal("Reading the movie's shelves...", Prompt(cut));
        Assert.Empty(cut.FindAll(".movie-shelves"));
    }

    // UI-6: the line says what Space, Enter and Esc do in the form.
    [Fact]
    public async Task Pick_ShelvesOpen_SayWhatSpaceEnterAndEscDo()
    {
        _catalog.Search = Found(Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);

        await OpenShelvesAsync(cut);

        Assert.Equal(TickTheShelves, Prompt(cut));
    }

    // Each Enter on a row asks for the shelves, and the first one's answer is on its way.
    [Fact]
    public async Task KeyDownEnter_TwiceWhileReading_ReadsOnce()
    {
        var reading = new TaskCompletionSource<IReadOnlyList<MovieShelfRow>>();
        _catalog.Search = Found(Halloween, Nightmare);
        _shelves.ListingMovieShelves = reading.Task;
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        var first = OpenShelvesAsync(cut, row: 1);

        var second = OpenShelvesAsync(cut, row: 0);

        await cut.InvokeAsync(() => reading.SetResult(ItsShelves));
        await first;
        await second;
        Assert.Equal(["ListMovieShelves 5"], _shelves.Calls);
        Assert.Single(cut.FindAll(".movie-shelves"));
    }

    // Build Plan 4.2's Done when, as the clerk does it: The Nightmare Before Christmas, ticked onto Christmas beside
    // Halloween, is saved on both, in one call to Core with the movie and the ticked shelves (ARCH-5), and the line
    // names them.
    [Fact]
    public async Task Save_DoneWhen_PutsTheNightmareBeforeChristmasOnHalloweenAndChristmas()
    {
        _catalog.Search = Found(Nightmare);
        _shelves.ListingMovieShelves = Task.FromResult<IReadOnlyList<MovieShelfRow>>([ChristmasRow, HalloweenRow]);
        _shelves.SettingMovieShelves = Task.FromResult<IReadOnlyList<MovieShelfRow>>([ChristmasRow with { HasTheMovie = true }, HalloweenRow]);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await OpenShelvesAsync(cut);
        await TickAsync(cut, ChristmasRow, ticked: true);

        await SaveAsync(cut);

        Assert.Equal(["ListMovieShelves 5", "SetMovieShelves 5 [9, 4]"], _shelves.Calls);
        Assert.Equal("Shelves for The Nightmare Before Christmas (1993): Christmas and Halloween.", Prompt(cut));
        Assert.Empty(cut.FindAll(".movie-shelves"));
    }

    // UI-6: the line names each shelf the movie is on now, as Core saved them and in Core's order, or says it's on
    // none, with the movie's title and year, as the form's group box names it. An unknown year is left out (DATA-3).
    [Theory]
    [InlineData("several", "Shelves for The Nightmare Before Christmas (1993): Christmas, Halloween and Dad's Staff Picks.")]
    [InlineData("two", "Shelves for The Nightmare Before Christmas (1993): Halloween and Dad's Staff Picks.")]
    [InlineData("one", "Shelves for The Nightmare Before Christmas (1993): Mom's Staff Picks.")]
    [InlineData("none", "Shelves for The Nightmare Before Christmas (1993): none.")]
    [InlineData("none, with no year", "Shelves for The Nightmare Before Christmas: none.")]
    public async Task Save_Saved_NamesEachShelfTheMovieIsOnNow(string onShelves, string line)
    {
        _catalog.Search = Found(onShelves == "none, with no year" ? Nightmare with { Year = null } : Nightmare);
        _shelves.SettingMovieShelves = Task.FromResult<IReadOnlyList<MovieShelfRow>>(onShelves switch
        {
            "several" => [ChristmasRow with { HasTheMovie = true }, HalloweenRow, DadsPicksRow, MomsPicksRow],
            "two" => ItsShelves,
            "one" => [ChristmasRow, HalloweenRow with { HasTheMovie = false }, DadsPicksRow with { HasTheMovie = false }, MomsPicksRow with { HasTheMovie = true }],
            _ => [ChristmasRow, HalloweenRow with { HasTheMovie = false }, DadsPicksRow with { HasTheMovie = false }, MomsPicksRow],
        });
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await OpenShelvesAsync(cut);

        await SaveAsync(cut);

        Assert.Equal(line, Prompt(cut));
    }

    // Save hands Core every shelf that's ticked, changed or not, and none ticked takes the movie off every shelf.
    [Fact]
    public async Task Save_EverythingUnticked_SavesNoShelves()
    {
        _catalog.Search = Found(Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await OpenShelvesAsync(cut);
        await TickAsync(cut, HalloweenRow, ticked: false);
        await TickAsync(cut, DadsPicksRow, ticked: false);

        await SaveAsync(cut);

        Assert.Equal(["ListMovieShelves 5", "SetMovieShelves 5 []"], _shelves.Calls);
    }

    // UI-3: the cursor goes back to the movie's row, here the second of three, so the arrow keys and Enter go on from
    // there.
    [Fact]
    public async Task Save_Saved_PutsTheCursorBackOnTheMoviesRow()
    {
        _catalog.Search = Found(Halloween, Nightmare, TradingPlaces);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        var nightmaresRow = RowIds(cut)[1];
        await Rows(cut)[1].FocusAsync(new FocusEventArgs());
        await OpenShelvesAsync(cut, row: 1);

        await SaveAsync(cut);

        AssertFocusedLast(nightmaresRow, focuses: 4); // the box, the list's first row, the first checkbox, then the row
        Assert.Equal("The Nightmare Before Christmas", Title(cut.Find(".movie-hits-list tbody tr.highlighted")));
    }

    // Once the shelves close, the list is back as the search left it, and no search runs by itself, even for a movie
    // that has just come off the shelf that was searched.
    [Fact]
    public async Task Save_OffTheShelfThatWasSearched_LeavesTheListAsTheSearchLeftIt()
    {
        _catalog.Search = Found(Halloween, Nightmare);
        _shelves.SettingMovieShelves = Task.FromResult<IReadOnlyList<MovieShelfRow>>([ChristmasRow, HalloweenRow with { HasTheMovie = false }, DadsPicksRow, MomsPicksRow]);
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, shelf: HalloweenShelf);
        await SubmitAsync(cut);
        var list = cut.FindComponent<MovieHits>().Instance;
        await OpenShelvesAsync(cut, row: 1);
        await TickAsync(cut, HalloweenRow, ticked: false);

        await SaveAsync(cut);

        Assert.Single(_catalog.Searches);
        Assert.Same(list, cut.FindComponent<MovieHits>().Instance);
        Assert.Equal(["Halloween", "The Nightmare Before Christmas"], Rows(cut).Select(Title));
        Assert.False(cut.Find(".movie-hits-list").HasAttribute("hidden"));
        Assert.False(cut.Find(".ask-the-clerk-search").HasAttribute("inert"));
        Assert.Equal(("", "", "", "", "4"), Boxes(cut));
    }

    // UI-6: the saving state, until Core has saved them.
    [Fact]
    public async Task Save_CoreStillSaving_SaysSo()
    {
        _catalog.Search = Found(Nightmare);
        _shelves.SettingMovieShelves = new TaskCompletionSource<IReadOnlyList<MovieShelfRow>>().Task;
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await OpenShelvesAsync(cut);

        _ = SaveAsync(cut);

        Assert.Equal("Saving the movie's shelves...", Prompt(cut));
    }

    // Enter in a checkbox presses Save each time, and the first one's shelves are on their way.
    [Fact]
    public async Task Save_TwiceWhileSaving_SavesOnce()
    {
        var saving = new TaskCompletionSource<IReadOnlyList<MovieShelfRow>>();
        _catalog.Search = Found(Nightmare);
        _shelves.SettingMovieShelves = saving.Task;
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await OpenShelvesAsync(cut);
        var first = SaveAsync(cut);

        var second = SaveAsync(cut);

        await cut.InvokeAsync(() => saving.SetResult(ItsShelves));
        await first;
        await second;
        Assert.Equal(["ListMovieShelves 5", "SetMovieShelves 5 [4, 2]"], _shelves.Calls);
    }

    // Not while Core saves: the shelves it's saving are the ones that count, so the form stays until they're saved,
    // then closes with the line naming them, as Return's Skip waits for its save.
    [Fact]
    public async Task Cancel_WhileSaving_ClosesNothingUntilSaved()
    {
        var saving = new TaskCompletionSource<IReadOnlyList<MovieShelfRow>>();
        _catalog.Search = Found(Nightmare);
        _shelves.SettingMovieShelves = saving.Task;
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await OpenShelvesAsync(cut);
        var save = SaveAsync(cut);

        await Checkboxes(cut)[0].KeyDownAsync(Escape);

        Assert.Single(cut.FindAll(".movie-shelves"));
        await cut.InvokeAsync(() => saving.SetResult(ItsShelves));
        await save;
        Assert.Equal("Shelves for The Nightmare Before Christmas (1993): Halloween and Dad's Staff Picks.", Prompt(cut));
        Assert.Empty(cut.FindAll(".movie-shelves"));
    }

    // Cancel and Esc close the shelves with nothing saved, whatever was ticked, and the cursor goes back to the movie's
    // row, here the second of three.
    [Theory]
    [InlineData("Cancel")]
    [InlineData("Esc in a checkbox")]
    public async Task Cancel_AfterATick_SavesNothingAndPutsTheCursorBackOnTheMoviesRow(string way)
    {
        _catalog.Search = Found(Halloween, Nightmare, TradingPlaces);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        var nightmaresRow = RowIds(cut)[1];
        await Rows(cut)[1].FocusAsync(new FocusEventArgs());
        await OpenShelvesAsync(cut, row: 1);
        await TickAsync(cut, ChristmasRow, ticked: true);

        await (way == "Cancel" ? FormButton(cut, "Cancel").ClickAsync(new MouseEventArgs()) : Checkboxes(cut)[0].KeyDownAsync(Escape));

        Assert.Equal(["ListMovieShelves 5"], _shelves.Calls);
        Assert.Empty(cut.FindAll(".movie-shelves"));
        AssertFocusedLast(nightmaresRow, focuses: 4); // the box, the list's first row, the first checkbox, then the row
        Assert.False(cut.Find(".movie-hits-list").HasAttribute("hidden"));
    }

    // Nothing changed, so the line says what the search found again.
    [Fact]
    public async Task Cancel_Closed_SaysWhatTheSearchFoundAgain()
    {
        _catalog.Search = Found(Halloween, Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await OpenShelvesAsync(cut, row: 1);

        await FormButton(cut, "Cancel").ClickAsync(new MouseEventArgs());

        Assert.Equal("2 movies. 2 are on the shelf.", Prompt(cut));
    }

    // The cursor moves back once: a redraw as the clerk picks never pulls it back to the row from the list they picked
    // in.
    [Fact]
    public async Task Change_PickAfterTheShelvesClose_LeavesTheCursorWhereTheClerkPutIt()
    {
        _catalog.Search = Found(Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await OpenShelvesAsync(cut);
        await FormButton(cut, "Cancel").ClickAsync(new MouseEventArgs());

        await cut.Find("#ask-the-clerk-genre").ChangeAsync(new ChangeEventArgs { Value = "Horror" });

        JSInterop.VerifyFocusAsyncInvoke(4); // the box, the list's first row, the first checkbox, then the row
    }

    // UI-6: with no shelves to go on, the form says so itself, with OK alone, so the line says what Enter does there.
    [Fact]
    public async Task Pick_NoShelvesToGoOn_SaysEnterGoesBackToTheList()
    {
        _catalog.Search = Found(Nightmare);
        _shelves.ListingMovieShelves = Task.FromResult<IReadOnlyList<MovieShelfRow>>([]);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);

        await OpenShelvesAsync(cut);

        Assert.Equal("Press Enter to go back to the list.", Prompt(cut));
        Assert.Equal("No shelves yet. The Store Manager adds members and seasonal shelves at F5.", Form(cut).QuerySelector("p")?.TextContent);
    }

    // OK, which Enter presses, and Esc each close it as a cancel: nothing to save, and the cursor back on the row.
    [Theory]
    [InlineData("OK")]
    [InlineData("Esc on OK")]
    public async Task Ok_NoShelvesToGoOn_SavesNothingAndPutsTheCursorBackOnTheMoviesRow(string way)
    {
        _catalog.Search = Found(Halloween, Nightmare);
        _shelves.ListingMovieShelves = Task.FromResult<IReadOnlyList<MovieShelfRow>>([]);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        var nightmaresRow = RowIds(cut)[1];
        await Rows(cut)[1].FocusAsync(new FocusEventArgs());
        await OpenShelvesAsync(cut, row: 1);

        await (way == "OK" ? FormButton(cut, "OK").ClickAsync(new MouseEventArgs()) : FormButton(cut, "OK").KeyDownAsync(Escape));

        Assert.Equal(["ListMovieShelves 5"], _shelves.Calls);
        Assert.Empty(cut.FindAll(".movie-shelves"));
        AssertFocusedLast(nightmaresRow, focuses: 4); // the box, the list's first row, OK, then the row
        Assert.Equal("2 movies. 2 are on the shelf.", Prompt(cut));
    }

    // A save's line lasts until the next search, which says what it found.
    [Fact]
    public async Task Submit_AfterASave_SaysWhatItFound()
    {
        _catalog.Search = Found(Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await OpenShelvesAsync(cut);
        await SaveAsync(cut);
        _catalog.Search = Found(Halloween, TradingPlaces);

        await SubmitAsync(cut);

        Assert.Equal("2 movies. 1 is on the shelf.", Prompt(cut));
    }

    // Or until Esc in the search, which starts over.
    [Fact]
    public async Task KeyDownEscape_InTheSearchAfterASave_StartsOver()
    {
        _catalog.Search = Found(Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await OpenShelvesAsync(cut);
        await SaveAsync(cut);

        await cut.Find(LookForBox).KeyDownAsync(Escape);

        Assert.Equal(BeforeASearch, Prompt(cut));
    }

    // Or until the next movie's shelves open, so closing those with nothing saved doesn't name the last movie's.
    [Fact]
    public async Task Cancel_TheNextMoviesShelvesAfterASave_SaysWhatTheSearchFound()
    {
        _catalog.Search = Found(Halloween, Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await OpenShelvesAsync(cut, row: 1);
        await SaveAsync(cut);
        await OpenShelvesAsync(cut, row: 0);

        await FormButton(cut, "Cancel").ClickAsync(new MouseEventArgs());

        Assert.Equal("2 movies. 2 are on the shelf.", Prompt(cut));
    }

    // Each opening gets a form of its own, which ticks what Core read, not what was ticked before.
    [Fact]
    public async Task Pick_AgainAfterACancel_TicksWhatCoreRead()
    {
        _catalog.Search = Found(Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await OpenShelvesAsync(cut);
        await TickAsync(cut, ChristmasRow, ticked: true);
        await FormButton(cut, "Cancel").ClickAsync(new MouseEventArgs());

        await OpenShelvesAsync(cut);

        Assert.Equal([false, true, true, false], Checkboxes(cut).Select(checkbox => checkbox.HasAttribute("checked")));
    }

    // Ask the Clerk takes no scans with the shelves open either, and its dialog leaves them open, as they were.
    [Fact]
    public async Task Scanned_WithTheShelvesOpen_SaysWhichScreenTakesItAndLeavesThemOpen()
    {
        _catalog.Search = Found(Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await OpenShelvesAsync(cut);
        await TickAsync(cut, ChristmasRow, ticked: true);

        await ScanAsync(cut, ScanKind.StoreLabel, "RV-00042");

        var dialog = Assert.IsType<Dialog>(_dialogs.Current);
        Assert.Equal(
            "Ask the Clerk doesn't take scans.\n\nPress F1 to check out, F2 to return a movie or F4 to add one, then scan again.",
            dialog.Message);
        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        Assert.Equal([true, true, true, false], Checkboxes(cut).Select(checkbox => checkbox.HasAttribute("checked")));
        Assert.Equal(["ListMovieShelves 5"], _shelves.Calls);
        Assert.Equal(TickTheShelves, Prompt(cut));
    }

    // One thing at a time, as while searching: a scan while Core reads or saves a movie's shelves is dropped.
    [Theory]
    [InlineData("reading")]
    [InlineData("saving")]
    public async Task Scanned_WhileReadingOrSavingAMoviesShelves_IsIgnoredAndLogged(string busy)
    {
        _catalog.Search = Found(Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        if (busy == "reading")
        {
            _shelves.ListingMovieShelves = new TaskCompletionSource<IReadOnlyList<MovieShelfRow>>().Task;
            _ = OpenShelvesAsync(cut);
        }
        else
        {
            _shelves.SettingMovieShelves = new TaskCompletionSource<IReadOnlyList<MovieShelfRow>>().Task;
            await OpenShelvesAsync(cut);
            _ = SaveAsync(cut);
        }

        await ScanAsync(cut, ScanKind.StoreLabel, "RV-00042");

        Assert.Null(_dialogs.Current);
        AssertLoggedIgnored("Ignored the StoreLabel RV-00042: Ask the Clerk is still busy or a dialog is open");
    }

    // While the screen is open, a failure goes to its ErrorBoundary, not to ErrorHub: a read of a movie's shelves, or a
    // save of them.
    [Theory]
    [InlineData("reading")]
    [InlineData("saving")]
    public async Task Pick_ReadOrSaveFails_HandsItToTheErrorBoundary(string failing)
    {
        _catalog.Search = Found(Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        var failure = Task.FromException<IReadOnlyList<MovieShelfRow>>(new InvalidOperationException("The call failed."));
        if (failing == "reading")
        {
            _shelves.ListingMovieShelves = failure;
        }
        else
        {
            _shelves.SettingMovieShelves = failure;
            await OpenShelvesAsync(cut);
        }

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => failing == "reading" ? OpenShelvesAsync(cut) : SaveAsync(cut));

        Assert.Equal("The call failed.", thrown.Message);
        Assert.False(_crashed);
    }

    // Leaving the screen with the shelves open loses what was ticked, and saves nothing.
    [Fact]
    public async Task Dispose_WithTheShelvesOpen_SavesNothing()
    {
        _catalog.Search = Found(Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await OpenShelvesAsync(cut);
        await TickAsync(cut, ChristmasRow, ticked: true);

        await DisposeComponentsAsync();

        Assert.Equal(["ListMovieShelves 5"], _shelves.Calls);
    }

    // Each call to Core for a movie's shelves takes the token leaving cancels (NET-7): the read and the save.
    [Theory]
    [InlineData("reading")]
    [InlineData("saving")]
    public async Task Dispose_WhileReadingOrSavingAMoviesShelves_CancelsTheCall(string busy)
    {
        _catalog.Search = Found(Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        if (busy == "reading")
        {
            _shelves.ListingMovieShelves = new TaskCompletionSource<IReadOnlyList<MovieShelfRow>>().Task;
            _ = OpenShelvesAsync(cut);
        }
        else
        {
            _shelves.SettingMovieShelves = new TaskCompletionSource<IReadOnlyList<MovieShelfRow>>().Task;
            await OpenShelvesAsync(cut);
            _ = SaveAsync(cut);
        }

        await DisposeComponentsAsync();

        Assert.True(_shelves.LastToken.IsCancellationRequested);
    }

    // ADR-0011: shelves read after the clerk left, rather than cancelled, open nothing and move no cursor, since the
    // screen has gone.
    [Fact]
    public async Task Dispose_MoviesShelvesAnswerAfterTheClerkLeft_ShowNothing()
    {
        var reading = new TaskCompletionSource<IReadOnlyList<MovieShelfRow>>();
        _catalog.Search = Found(Nightmare);
        _shelves.ListingMovieShelves = reading.Task;
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        var opening = OpenShelvesAsync(cut);
        await DisposeComponentsAsync();

        reading.SetResult(ItsShelves);
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the screen's own continuation
        await opening;

        JSInterop.VerifyFocusAsyncInvoke(2); // the box, as the screen opened, and the list's first row, and no checkbox
        Assert.Null(_dialogs.Current);
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // And shelves saved after the clerk left move no cursor back to a row that's gone.
    [Fact]
    public async Task Dispose_SaveAnswersAfterTheClerkLeft_ShowsNothing()
    {
        var saving = new TaskCompletionSource<IReadOnlyList<MovieShelfRow>>();
        _catalog.Search = Found(Nightmare);
        _shelves.SettingMovieShelves = saving.Task;
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        await OpenShelvesAsync(cut);
        var save = SaveAsync(cut);
        await DisposeComponentsAsync();

        saving.SetResult(ItsShelves);
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the screen's own continuation
        await save;

        JSInterop.VerifyFocusAsyncInvoke(3); // the box, the list's first row and the first checkbox, and not the row again
        Assert.Null(_dialogs.Current);
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // #83's route: a read or a save of a movie's shelves that fails after the clerk left has no screen to show it on,
    // so it goes to ErrorHub, as the exception the call threw (OBS-4).
    [Theory]
    [InlineData("reading")]
    [InlineData("saving")]
    public async Task Dispose_WhileReadingOrSavingAMoviesShelves_ReportsAFailureToErrorHub(string busy)
    {
        var answer = new TaskCompletionSource<IReadOnlyList<MovieShelfRow>>();
        _catalog.Search = Found(Nightmare);
        var cut = Render<AskTheClerk>();
        await SubmitAsync(cut);
        Task call;
        if (busy == "reading")
        {
            _shelves.ListingMovieShelves = answer.Task;
            call = OpenShelvesAsync(cut);
        }
        else
        {
            _shelves.SettingMovieShelves = answer.Task;
            await OpenShelvesAsync(cut);
            call = SaveAsync(cut);
        }

        await DisposeComponentsAsync();
        var failure = new InvalidOperationException("The call failed.");

        answer.SetException(failure);
        await call;

        Assert.True(_crashed);
        Assert.Equal([(LogLevel.Error, "Unhandled exception from AskTheClerk; reported to the shell", (Exception?)failure)], _errorLog.Entries);
    }

    // The shelves' read, as the screen opens, fails to the ErrorBoundary while the screen is open, as the genres' does.
    [Fact]
    public async Task Render_ShelvesReadFails_HandsItToTheErrorBoundary()
    {
        var shelves = new TaskCompletionSource<IReadOnlyList<ShelfRow>>();
        _shelves.ListingTonights = shelves.Task;
        _ = Render<AskTheClerk>();

        await Renderer.Dispatcher.InvokeAsync(() => shelves.SetException(new InvalidOperationException("The read failed.")));

        Assert.True(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
    }

    // The shelves' read takes the token leaving cancels (NET-7).
    [Fact]
    public async Task Dispose_WhileReadingTheShelves_CancelsTheRead()
    {
        _shelves.ListingTonights = new TaskCompletionSource<IReadOnlyList<ShelfRow>>().Task;
        _ = Render<AskTheClerk>();

        await DisposeComponentsAsync();

        Assert.True(Assert.Single(_shelves.Tokens).IsCancellationRequested);
    }

    // A clerk who leaves while the genres are read has gone before the shelves' turn, so nothing reads them.
    [Fact]
    public async Task Dispose_WhileReadingTheGenres_ReadsNoShelves()
    {
        var genres = new TaskCompletionSource<IReadOnlyList<string>>();
        _catalog.Genres = genres.Task;
        _ = Render<AskTheClerk>();
        await DisposeComponentsAsync();

        await Renderer.Dispatcher.InvokeAsync(() => genres.SetCanceled(_catalog.LastToken)); // as EF Core ends a cancelled query

        Assert.Equal(0, _shelves.TonightsReads);
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // The shelves wait in the busy line too, so a read that fails after the clerk left takes the genres' route.
    [Fact]
    public async Task Dispose_WhileReadingTheShelves_ReportsAFailureToErrorHub()
    {
        var shelves = new TaskCompletionSource<IReadOnlyList<ShelfRow>>();
        _shelves.ListingTonights = shelves.Task;
        _ = Render<AskTheClerk>();
        await DisposeComponentsAsync();
        var failure = new InvalidOperationException("The read failed.");

        await Renderer.Dispatcher.InvokeAsync(() => shelves.SetException(failure));

        Assert.True(_crashed);
        Assert.Equal([(LogLevel.Error, "Unhandled exception from AskTheClerk; reported to the shell", (Exception?)failure)], _errorLog.Entries);
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    private static Task<SearchResult> Found(params MovieHit[] hits) =>
        Task.FromResult(new SearchResult(SearchStatus.Searched, hits, MoreMatched: false));

    // A search Core didn't run, such as for a year that isn't one.
    private static Task<SearchResult> DidNotRun(SearchStatus status) =>
        Task.FromResult(new SearchResult(status, [], MoreMatched: false));

    // Types in each box and picks from each list, as the clerk does. A list takes its option's value, which is blank
    // for Any, and a shelf's Id for a shelf.
    private static async Task FillInAsync(
        IRenderedComponent<AskTheClerk> cut, string text = "", string genre = "", string year = "", string rating = "", ShelfRow? shelf = null)
    {
        await cut.Find(LookForBox).InputAsync(text);
        await cut.Find("#ask-the-clerk-genre").ChangeAsync(new ChangeEventArgs { Value = genre });
        await cut.Find(YearBox).InputAsync(year);
        await cut.Find("#ask-the-clerk-rating").ChangeAsync(new ChangeEventArgs { Value = rating });
        await PickShelfAsync(cut, shelf);
    }

    // Picks a shelf from the Shelf list, or Any for null.
    private static Task PickShelfAsync(IRenderedComponent<AskTheClerk> cut, ShelfRow? shelf) =>
        cut.Find(ShelfList).ChangeAsync(new ChangeEventArgs { Value = shelf?.Id.ToString(CultureInfo.InvariantCulture) ?? "" });

    // Enter in the search. Nothing here opens a dialog, which would wait for someone to close it, and the test with it,
    // so a dialog fails the test before it waits, as Return's SaveAsync does.
    private Task SubmitAsync(IRenderedComponent<AskTheClerk> cut)
    {
        var search = cut.Find(".ask-the-clerk-search").SubmitAsync();
        Assert.Null(_dialogs.Current?.Message);
        return search;
    }

    private async Task SearchForAsync(IRenderedComponent<AskTheClerk> cut, string text)
    {
        await cut.Find(LookForBox).InputAsync(text);
        await SubmitAsync(cut);
    }

    // Enter on a movie's row, here the first, which opens its shelves once Core has read them. Nothing here opens a
    // dialog, so a dialog fails the test before it waits.
    private Task OpenShelvesAsync(IRenderedComponent<AskTheClerk> cut, int row = 0)
    {
        var opening = Rows(cut)[row].KeyDownAsync(Enter);
        Assert.Null(_dialogs.Current?.Message);
        return opening;
    }

    // Save in the movie's shelves, as Enter in a checkbox presses it. Nothing here opens a dialog either.
    private Task SaveAsync(IRenderedComponent<AskTheClerk> cut)
    {
        var save = FormButton(cut, "Save").ClickAsync(new MouseEventArgs());
        Assert.Null(_dialogs.Current?.Message);
        return save;
    }

    // Ticks or unticks a shelf's checkbox, as Space does in WebView2: the box changes, and Blazor hears it.
    private static Task TickAsync(IRenderedComponent<AskTheClerk> cut, MovieShelfRow shelf, bool ticked) =>
        cut.Find($"#movie-shelves-{shelf.Id}").ChangeAsync(new ChangeEventArgs { Value = ticked });

    private static IElement Form(IRenderedComponent<AskTheClerk> cut) => cut.Find("fieldset.movie-shelves");

    private static IElement FormButton(IRenderedComponent<AskTheClerk> cut, string text) =>
        cut.FindAll(".movie-shelves button").Single(button => button.TextContent == text);

    private static IReadOnlyList<IElement> Checkboxes(IRenderedComponent<AskTheClerk> cut) =>
        cut.FindAll(".movie-shelves input[type=checkbox]");

    // The ID of each row's @ref. bUnit writes it into the markup only on the list's first render, so a test reads them
    // straight after the search.
    private static List<string?> RowIds(IRenderedComponent<AskTheClerk> cut) =>
        [.. Rows(cut).Select(row => row.GetAttribute("blazor:elementreference"))];

    // A scan, as the scanner's thread raises it, then a turn of the screen's own thread, so the screen has handled it
    // before the test looks.
    private async Task ScanAsync(IRenderedComponent<AskTheClerk> cut, ScanKind kind, string code)
    {
        _scanner.Emit(new Scan(kind, code));
        await cut.InvokeAsync(() => { });
    }

    // The ID of an element's @ref. bUnit writes it into the markup only on the element's first render, so a test reads
    // it before anything redraws the element.
    private static string? ElementId(IRenderedComponent<AskTheClerk> cut, string selector) =>
        cut.Find(selector).GetAttribute("blazor:elementreference");

    // UI-3: the element focused last, from all the focus calls so far.
    private void AssertFocusedLast(string? elementId, int focuses)
    {
        var focused = Assert.IsType<ElementReference>(JSInterop.VerifyFocusAsyncInvoke(focuses)[^1].Arguments[0]);
        Assert.Equal(elementId, focused.Id);
    }

    // What the four boxes and the Shelf list hold: each text box's text, and each list's pick, which is blank for Any
    // and a shelf's Id for a shelf.
    private static (string? Text, string? Genre, string? Year, string? Rating, string? Shelf) Boxes(IRenderedComponent<AskTheClerk> cut) =>
        (cut.Find(LookForBox).GetAttribute("value"),
            cut.Find("#ask-the-clerk-genre").GetAttribute("value"),
            cut.Find(YearBox).GetAttribute("value"),
            cut.Find("#ask-the-clerk-rating").GetAttribute("value"),
            cut.Find(ShelfList).GetAttribute("value"));

    // The choice a list shows: the one whose value Blazor gave the list.
    private static string Chosen(IElement list) =>
        list.QuerySelectorAll("option").Single(option => option.GetAttribute("value") == list.GetAttribute("value")).TextContent;

    private static IReadOnlyList<IElement> Rows(IRenderedComponent<AskTheClerk> cut) => cut.FindAll(".movie-hits-list tbody tr");

    // The first cell holds the thumbnail and the title, which is its only text.
    private static string Title(IElement row) => row.Children[0].TextContent;

    private static string Prompt(IRenderedComponent<AskTheClerk> cut) => cut.Find(".ask-the-clerk-prompt").TextContent;

    private void AssertLoggedIgnored(string message) => Assert.Contains((LogLevel.Debug, message, (Exception?)null), _log.Entries);
}
