using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Rewind.Core.Catalog;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class MovieHitsTests : BunitContext
{
    private static readonly MovieHit Halloween = new(1, "Halloween", 1978, "R", OnShelf: true, Thumb: [0xFF, 0xD8, 0xFF, 0xE0]);
    private static readonly MovieHit HomeMovies = new(2, "Home movies", null, null, OnShelf: false, Thumb: null);
    private static readonly MovieHit Scream = new(3, "Scream", 1996, "R", OnShelf: true, Thumb: null);

    private readonly List<MovieHit> _picked = [];
    private int _escapes;

    // DATA-3: an unknown year or rating is an empty cell, never 0 or NR.
    [Fact]
    public void Render_Hits_ListsEachWithItsYearRatingAndShelf()
    {
        var cut = RenderList();

        Assert.Equal(
            [("Halloween", "1978", "R", "On the shelf"), ("Home movies", "", "", "Checked out")],
            Rows(cut).Select(row => (Title(row), Cell(row, 1), Cell(row, 2), Cell(row, 3))));
    }

    [Fact]
    public void Render_Hits_HeadsEachColumn()
    {
        var cut = RenderList();

        Assert.Equal(["Title", "Year", "Rating", "Status"], cut.FindAll("thead th").Select(heading => heading.TextContent));
    }

    // keyboard.js moves the focus through a 98.css interactive table's rows, which 98.css draws in a sunken panel.
    [Fact]
    public void Render_Hits_IsA98List()
    {
        var cut = RenderList();

        Assert.Equal(2, cut.FindAll(".sunken-panel > table.interactive > tbody > tr").Count);
    }

    // SEC-6: the thumbnail is in the page itself, as the picker's posters are, so the web view never fetches one.
    [Fact]
    public void Render_Thumb_IsPartOfThePage()
    {
        var cut = RenderList();

        var thumb = Assert.Single(cut.FindAll("img"));
        Assert.Equal("data:image/jpeg;base64,/9j/4A==", thumb.GetAttribute("src"));
        Assert.Equal(thumb, Rows(cut)[0].QuerySelector(".movie-hits-title img"));
    }

    // A hit without a thumbnail keeps its place, so the titles line up.
    [Fact]
    public void Render_HitWithoutThumb_KeepsItsPlace()
    {
        var cut = RenderList();

        Assert.NotNull(Rows(cut)[1].QuerySelector(".movie-hits-title .movie-hits-no-thumb"));
        Assert.Null(Rows(cut)[1].QuerySelector("img"));
    }

    // Each src is made once, as the list opens, not again as the highlight moves or the screen redraws: it's a
    // thumbnail's bytes in base64. A src made again would show the bytes as they are now.
    [Fact]
    public async Task Render_Redraws_KeepsEachThumbsSrc()
    {
        byte[] bytes = [0xFF, 0xD8, 0xFF, 0xE0];
        var cut = RenderList([Halloween with { Thumb = bytes }, HomeMovies]);
        bytes[^1] = 0xE1;

        await Rows(cut)[1].FocusAsync(new FocusEventArgs());
        cut.Render();

        Assert.Equal("data:image/jpeg;base64,/9j/4A==", cut.Find("img").GetAttribute("src"));
    }

    // UI-3: the arrow keys work as soon as the list opens.
    [Fact]
    public void Render_Hits_FocusesAndHighlightsTheFirst()
    {
        var cut = RenderList();
        var firstRow = Rows(cut)[0].GetAttribute("blazor:elementreference"); // bUnit writes it on the first render only

        var focused = Assert.IsType<ElementReference>(JSInterop.VerifyFocusAsyncInvoke().Arguments[0]);
        Assert.Equal(firstRow, focused.Id);
        Assert.Equal("Halloween", Title(Highlighted(cut)));
        Assert.Equal(["0", "-1"], Rows(cut).Select(row => row.GetAttribute("tabindex")));
        Assert.Equal(["true", "false"], Rows(cut).Select(row => row.GetAttribute("aria-selected")));
    }

    // keyboard.js moves the focus with the arrow keys, Home and End, and the highlight follows it.
    [Fact]
    public async Task Focus_SecondRow_HighlightsIt()
    {
        var cut = RenderList();

        await Rows(cut)[1].FocusAsync(new FocusEventArgs());

        Assert.Equal("Home movies", Title(Highlighted(cut)));
        Assert.Equal(["-1", "0"], Rows(cut).Select(row => row.GetAttribute("tabindex")));
        Assert.Equal(["false", "true"], Rows(cut).Select(row => row.GetAttribute("aria-selected")));
    }

    // The list takes the focus once, as it opens, so a redraw never pulls it back from the row the clerk is on, or
    // moves the highlight off it.
    [Fact]
    public async Task Render_Redraws_LeavesTheFocusAndHighlightAlone()
    {
        var cut = RenderList();

        await Rows(cut)[1].FocusAsync(new FocusEventArgs());
        cut.Render();

        JSInterop.VerifyFocusAsyncInvoke();
        Assert.Equal("Home movies", Title(Highlighted(cut)));
    }

    // UI-3: Esc hands the cursor back to the screen, for its search box.
    [Fact]
    public async Task KeyDownEscape_OnARow_CallsBack()
    {
        var cut = RenderList();

        await Rows(cut)[1].KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(1, _escapes);
    }

    // UI-3: Enter on a row picks its movie, for the screen to show its shelves. The key goes down on a row, so the hit is
    // that row's.
    [Fact]
    public async Task KeyDownEnter_OnARow_PicksItsHit()
    {
        var cut = RenderList();

        await Rows(cut)[1].KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal([HomeMovies], _picked);
        Assert.Equal(0, _escapes);
    }

    [Fact]
    public async Task DoubleClick_ARow_PicksItsHit()
    {
        var cut = RenderList();

        await Rows(cut)[1].DoubleClickAsync(new MouseEventArgs());

        Assert.Equal([HomeMovies], _picked);
        Assert.Equal(0, _escapes);
    }

    // With no callback there's nothing to pick for, so Enter does nothing, as in Build Plan 4.1.
    [Fact]
    public async Task KeyDownEnter_NothingToPickFor_DoesNothing()
    {
        var cut = RenderList([Halloween, HomeMovies], pickable: false);

        await Rows(cut)[0].KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(0, _escapes);
        Assert.Equal("Halloween", Title(Highlighted(cut)));
    }

    // The row hears a double-click, and with no callback there's nothing to pick for, so it does nothing with it.
    [Fact]
    public async Task DoubleClick_NothingToPickFor_DoesNothing()
    {
        var cut = RenderList([Halloween, HomeMovies], pickable: false);

        await Rows(cut)[1].DoubleClickAsync(new MouseEventArgs());

        Assert.Equal(0, _escapes);
        Assert.Equal("Halloween", Title(Highlighted(cut)));
    }

    [Fact]
    public async Task KeyDown_AnyOtherKey_DoesNothing()
    {
        var cut = RenderList();

        await Rows(cut)[0].KeyDownAsync(new KeyboardEventArgs { Key = "a" });

        Assert.Empty(_picked);
        Assert.Equal(0, _escapes);
        Assert.Equal("Halloween", Title(Highlighted(cut)));
    }

    // UI-3: the movie's shelves take the focus from the list, and the screen puts it back on the row the clerk was on,
    // here the middle one of three.
    [Fact]
    public async Task FocusHighlightedRowAsync_MiddleRowHighlighted_FocusesItAgain()
    {
        var cut = RenderList([Halloween, HomeMovies, Scream]);
        var middleRow = Rows(cut)[1].GetAttribute("blazor:elementreference"); // bUnit writes it on the first render only
        await Rows(cut)[1].FocusAsync(new FocusEventArgs());

        await cut.InvokeAsync(cut.Instance.FocusHighlightedRowAsync);

        var focused = Assert.IsType<ElementReference>(JSInterop.VerifyFocusAsyncInvoke(calledTimes: 2)[^1].Arguments[0]);
        Assert.Equal(middleRow, focused.Id);
    }

    // With no hits there's no row, and the focus stays where it is.
    [Fact]
    public async Task FocusHighlightedRowAsync_NoHits_FocusesNothing()
    {
        var cut = RenderList([]);

        await cut.InvokeAsync(cut.Instance.FocusHighlightedRowAsync);

        Assert.Empty(JSInterop.Invocations);
    }

    // UI-10: a title from TMDB renders as text, so markup in it shows as typed and draws nothing.
    [Fact]
    public void Render_TitleWithMarkup_ShowsItAsTyped()
    {
        const string Typed = "<i>Halloween</i> & <b>Friends</b>";
        var cut = RenderList([Halloween with { Title = Typed }]);

        Assert.Equal(Typed, Title(Assert.Single(Rows(cut))));
        Assert.Empty(cut.FindAll("i, b"));
    }

    // UI-6: what a search that found nothing says is the screen's to show.
    [Fact]
    public void Render_NoHits_ShowsAndFocusesNothing()
    {
        var cut = RenderList([]);

        Assert.Empty(cut.Nodes);
        Assert.Empty(JSInterop.Invocations);
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    private IRenderedComponent<MovieHits> RenderList() => RenderList([Halloween, HomeMovies]);

    // Pickable unless a test says otherwise, as the screen that shows a movie's shelves has it.
    private IRenderedComponent<MovieHits> RenderList(IReadOnlyList<MovieHit> hits, bool pickable = true) =>
        Render<MovieHits>(parameters =>
        {
            parameters
                .Add(p => p.Hits, hits)
                .Add(p => p.OnEscape, EventCallback.Factory.Create(this, () => _escapes++));
            if (pickable)
            {
                parameters.Add(p => p.OnPick, EventCallback.Factory.Create<MovieHit>(this, _picked.Add));
            }
        });

    private static IReadOnlyList<IElement> Rows(IRenderedComponent<MovieHits> cut) =>
        cut.FindAll(".movie-hits-list tbody tr");

    private static IElement Highlighted(IRenderedComponent<MovieHits> cut) =>
        cut.Find(".movie-hits-list tbody tr.highlighted");

    // The first cell holds the thumbnail, or its empty box, and the title, which is its only text.
    private static string Title(IElement row) => Cell(row, 0);

    private static string Cell(IElement row, int column) => row.Children[column].TextContent;
}
