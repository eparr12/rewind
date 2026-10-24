using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Rewind.Core.Metadata;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class TmdbPickerTests : BunitContext
{
    private static readonly TmdbMatch Halloween1978 =
        new(948, "Halloween", 1978, "A masked killer comes home.", "/wijlZ3HaYMvlDTPqJoTCWKFkCPU.jpg")
        {
            Poster = [0xFF, 0xD8, 0xFF, 0xE0],
        };

    private static readonly TmdbMatch Halloween2018 = new(424139, "Halloween", 2018, "Forty years on.", null);

    private readonly List<TmdbMatch> _picked = [];
    private bool _cancelled;

    // UI-3: the arrow keys and Enter work as soon as the list opens.
    [Fact]
    public void Render_Matches_HighlightsAndFocusesTheFirst()
    {
        var cut = RenderPicker();

        Assert.Equal("Halloween", Highlighted(cut).QuerySelector(".tmdb-picker-title span")?.TextContent);
        Assert.Equal("1978", Highlighted(cut).Children[1].TextContent);
        JSInterop.VerifyFocusAsyncInvoke();
    }

    // keyboard.js moves the focus with the arrow keys, and the highlight follows it.
    [Fact]
    public async Task Focus_SecondRow_HighlightsIt()
    {
        var cut = RenderPicker();

        await Rows(cut)[1].FocusAsync(new FocusEventArgs());

        Assert.Equal("2018", Highlighted(cut).Children[1].TextContent);
        Assert.Equal(["-1", "0"], Rows(cut).Select(row => row.GetAttribute("tabindex")));
    }

    [Fact]
    public async Task KeyDownEnter_OnARow_PicksIt()
    {
        var cut = RenderPicker();

        await Rows(cut)[1].KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal([Halloween2018], _picked);
    }

    [Fact]
    public async Task DoubleClick_ARow_PicksIt()
    {
        var cut = RenderPicker();

        await Rows(cut)[1].DoubleClickAsync(new MouseEventArgs());

        Assert.Equal([Halloween2018], _picked);
    }

    [Fact]
    public async Task ClickAddMovie_SecondRowHighlighted_PicksIt()
    {
        var cut = RenderPicker();
        await Rows(cut)[1].FocusAsync(new FocusEventArgs());

        await Button(cut, "Add Movie").ClickAsync(new MouseEventArgs());

        Assert.Equal([Halloween2018], _picked);
    }

    // UI-3: Esc cancels.
    [Fact]
    public async Task KeyDownEscape_InThePicker_Cancels()
    {
        var cut = RenderPicker();

        await cut.Find(".tmdb-picker").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(_cancelled);
        Assert.Empty(_picked);
    }

    // SEC-6: the poster is in the page itself, so the web view never fetches from TMDB.
    [Fact]
    public void Render_Poster_IsPartOfThePage()
    {
        var cut = RenderPicker();

        var poster = Assert.Single(cut.FindAll(".tmdb-picker img"));
        Assert.Equal("data:image/jpeg;base64,/9j/4A==", poster.GetAttribute("src"));
    }

    // A match without a poster keeps the poster's space, so the titles line up.
    [Fact]
    public void Render_MatchWithoutPoster_KeepsItsPlace()
    {
        var cut = RenderPicker();

        Assert.NotNull(Rows(cut)[1].QuerySelector(".tmdb-picker-no-poster"));
    }

    private IRenderedComponent<TmdbPicker> RenderPicker() =>
        Render<TmdbPicker>(parameters => parameters
            .Add(p => p.Matches, [Halloween1978, Halloween2018])
            .Add(p => p.OnPick, EventCallback.Factory.Create<TmdbMatch>(this, _picked.Add))
            .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => _cancelled = true)));

    private static IReadOnlyList<IElement> Rows(IRenderedComponent<TmdbPicker> cut) =>
        cut.FindAll(".tmdb-picker tbody tr");

    private static IElement Highlighted(IRenderedComponent<TmdbPicker> cut) =>
        cut.Find(".tmdb-picker tbody tr.highlighted");

    private static IElement Button(IRenderedComponent<TmdbPicker> cut, string text) =>
        cut.FindAll("button").Single(button => button.TextContent == text);
}
