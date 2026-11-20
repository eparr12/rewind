using System.Diagnostics;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rewind.Core.Catalog;
using Rewind.Core.Devices;
using Rewind.Core.Scans;
using Rewind.Core.Sounds;
using Rewind.UI.Dialogs;
using Rewind.UI.Errors;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class AskTheClerkTests : BunitContext
{
    private const string BeforeASearch = "Type part of a title or a name, or pick a genre, a year or a rating, then press Enter.";
    private const string NotAYear = "The year is four digits, such as 1978, or blank.";
    private const string NotARating = "Pick a rating from the list, or Any.";
    private const string LookForBox = "#ask-the-clerk-text";
    private const string YearBox = "#ask-the-clerk-year";

    private static readonly KeyboardEventArgs Escape = new() { Key = "Escape" };

    // Jamie Lee Curtis's movies, as Core finds them by her name, some on the shelf and some checked out. Each has a
    // thumbnail of its own, so a list that kept another search's would show it.
    private static readonly MovieHit Halloween = new(1, "Halloween", 1978, "R", OnShelf: true, Thumb: [0xFF, 0xD8, 0xFF, 0xE0]);
    private static readonly MovieHit TradingPlaces = new(2, "Trading Places", 1983, "R", OnShelf: false, Thumb: [0xFF, 0xD8, 0xFF, 0xE1]);
    private static readonly MovieHit FreakyFriday = new(3, "Freaky Friday", 2003, "PG", OnShelf: true, Thumb: [0xFF, 0xD8, 0xFF, 0xE2]);
    private static readonly MovieHit TrueLies = new(4, "True Lies", 1994, "R", OnShelf: false, Thumb: [0xFF, 0xD8, 0xFF, 0xE3]);

    private readonly FakeCatalogSearch _catalog = new();
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
        Services.AddSingleton<ICatalogSearch>(_catalog);
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

    // ARCH-5: one call to Core for each search, with each box in its own place. A text box submits its form by itself,
    // and keyboard.js has Enter in a list submit the list's own form, so each box and list is in the one Search submits.
    [Theory]
    [InlineData(LookForBox)]
    [InlineData("#ask-the-clerk-genre")]
    [InlineData(YearBox)]
    [InlineData("#ask-the-clerk-rating")]
    public async Task Submit_EnterInEachBoxOrList_SearchesOnceWithEachBoxInItsPlace(string control)
    {
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, "curtis", "Horror", "1978", "R");
        var form = cut.Find(control).Closest("form");
        Assert.NotNull(form);

        await form.SubmitAsync();

        Assert.Equal([("curtis", "Horror", "1978", "R")], _catalog.Searches);
    }

    [Fact]
    public async Task ClickSearch_BoxesFilledIn_SearchesOnceWithEachBoxInItsPlace()
    {
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, "curtis", "Horror", "1978", "R");

        await cut.Find("button").ClickAsync(new MouseEventArgs());

        Assert.Equal([("curtis", "Horror", "1978", "R")], _catalog.Searches);
    }

    // Any is blank, and Core reads a blank box as nothing picked, so nothing typed or picked lists the whole catalog.
    [Fact]
    public async Task Submit_NothingTypedOrPicked_SendsEachBoxBlank()
    {
        var cut = Render<AskTheClerk>();

        await SubmitAsync(cut);

        Assert.Equal([("", "", "", "")], _catalog.Searches);
    }

    // SEC-5: the screen hands Core what was typed, spaces and all, and Core cleans it.
    [Fact]
    public async Task Submit_SpacesAroundWhatWasTyped_SendsItAsTyped()
    {
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, " Jamie Lee Curtis ", year: "1978 ");

        await SubmitAsync(cut);

        Assert.Equal([(" Jamie Lee Curtis ", "", "1978 ", "")], _catalog.Searches);
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

    // UI-3: Esc in the search clears it, from any box, list or button: the text, both lists back on Any, and the hits.
    [Theory]
    [InlineData(LookForBox)]
    [InlineData("#ask-the-clerk-genre")]
    [InlineData(YearBox)]
    [InlineData("#ask-the-clerk-rating")]
    [InlineData("button")]
    public async Task KeyDownEscape_InTheSearch_ClearsTheBoxesThePicksAndTheHits(string control)
    {
        _catalog.Search = Found(Halloween, TradingPlaces);
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, "curtis", "Horror", "1978", "R");
        await SubmitAsync(cut);

        await cut.Find(control).KeyDownAsync(Escape);

        Assert.Equal(("", "", "", ""), Boxes(cut));
        Assert.Equal(["Any", "Any"], cut.FindAll("select").Select(Chosen));
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
        await FillInAsync(cut, "curtis", "Horror", "1978", "R");
        var search = SubmitAsync(cut);

        await cut.Find(LookForBox).KeyDownAsync(Escape);

        await cut.InvokeAsync(() => searched.SetResult(new SearchResult(SearchStatus.Searched, [Halloween], MoreMatched: false)));
        await search;
        Assert.Equal(("curtis", "Horror", "1978", "R"), Boxes(cut));
        Assert.Equal(["Halloween"], Rows(cut).Select(Title));
    }

    [Fact]
    public async Task KeyDown_AnyOtherKeyInTheSearch_ClearsNothing()
    {
        _catalog.Search = Found(Halloween);
        var cut = Render<AskTheClerk>();
        await FillInAsync(cut, "curtis", "Horror", "1978", "R");
        await SubmitAsync(cut);

        await cut.Find(LookForBox).KeyDownAsync(new KeyboardEventArgs { Key = "a" });

        Assert.Equal(("curtis", "Horror", "1978", "R"), Boxes(cut));
        Assert.Equal(["Halloween"], Rows(cut).Select(Title));
    }

    // UI-3: Esc on the list puts the cursor back in the Look for box, to search again.
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
        await FillInAsync(cut, "curtis", "Horror", "1978", "R");
        await SubmitAsync(cut);

        await Rows(cut)[1].KeyDownAsync(Escape);

        Assert.Equal(("curtis", "Horror", "1978", "R"), Boxes(cut));
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

    private static Task<SearchResult> Found(params MovieHit[] hits) =>
        Task.FromResult(new SearchResult(SearchStatus.Searched, hits, MoreMatched: false));

    // A search Core didn't run, such as for a year that isn't one.
    private static Task<SearchResult> DidNotRun(SearchStatus status) =>
        Task.FromResult(new SearchResult(status, [], MoreMatched: false));

    // Types in each box and picks from each list, as the clerk does. A list takes its option's value, which is blank
    // for Any.
    private static async Task FillInAsync(IRenderedComponent<AskTheClerk> cut, string text = "", string genre = "", string year = "", string rating = "")
    {
        await cut.Find(LookForBox).InputAsync(text);
        await cut.Find("#ask-the-clerk-genre").ChangeAsync(new ChangeEventArgs { Value = genre });
        await cut.Find(YearBox).InputAsync(year);
        await cut.Find("#ask-the-clerk-rating").ChangeAsync(new ChangeEventArgs { Value = rating });
    }

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

    // What the four boxes hold: each text box's text, and each list's pick, which is blank for Any.
    private static (string? Text, string? Genre, string? Year, string? Rating) Boxes(IRenderedComponent<AskTheClerk> cut) =>
        (cut.Find(LookForBox).GetAttribute("value"),
            cut.Find("#ask-the-clerk-genre").GetAttribute("value"),
            cut.Find(YearBox).GetAttribute("value"),
            cut.Find("#ask-the-clerk-rating").GetAttribute("value"));

    // The choice a list shows: the one whose value Blazor gave the list.
    private static string Chosen(IElement list) =>
        list.QuerySelectorAll("option").Single(option => option.GetAttribute("value") == list.GetAttribute("value")).TextContent;

    private static IReadOnlyList<IElement> Rows(IRenderedComponent<AskTheClerk> cut) => cut.FindAll(".movie-hits-list tbody tr");

    // The first cell holds the thumbnail and the title, which is its only text.
    private static string Title(IElement row) => row.Children[0].TextContent;

    private static string Prompt(IRenderedComponent<AskTheClerk> cut) => cut.Find(".ask-the-clerk-prompt").TextContent;

    private void AssertLoggedIgnored(string message) => Assert.Contains((LogLevel.Debug, message, (Exception?)null), _log.Entries);
}
