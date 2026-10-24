using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Rewind.Core.Intake;
using Rewind.Core.Metadata;
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

    private readonly FakeMovieIntake _intake = new();
    private readonly DialogService _dialogs = new(new FakeSoundPlayer());

    public IntakeTests()
    {
        Services.AddSingleton<IMovieIntake>(_intake);
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
        Assert.Equal("Type the title from the DVD case, then press Enter.", cut.Find(".intake-prompt").TextContent);
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
            "Added to the collection. Type the next title, then press Enter.",
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
            "That movie is already in the collection. Type the next title, then press Enter.",
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
        Assert.Equal("Type the title from the DVD case, then press Enter.", cut.Find(".intake-prompt").TextContent);
    }

    private static Task<TmdbSearch> Found(params TmdbMatch[] matches) =>
        Task.FromResult(new TmdbSearch(TmdbStatus.Ok, matches));

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
