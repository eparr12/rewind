using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Rewind.Core.Checkout;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class WhichMovieTests : BunitContext
{
    private static readonly MovieInCase Halloween = new(1, "Halloween", 1978);
    private static readonly MovieInCase HomeMovies = new(2, "Home movies", null);

    private readonly List<MovieInCase> _picked = [];
    private bool _cancelled;

    // A remake shares its title, so the year is what tells two rows apart. An unknown year stays blank (DATA-3).
    [Fact]
    public void Render_Movies_ListsEachWithItsYear()
    {
        var cut = RenderPicker();

        Assert.Equal(
            [("Halloween", "1978"), ("Home movies", "")],
            Rows(cut).Select(row => (row.Children[0].TextContent, row.Children[1].TextContent)));
    }

    // UI-3: the arrow keys and Enter work as soon as the list opens.
    [Fact]
    public void Render_Movies_HighlightsAndFocusesTheFirst()
    {
        var cut = RenderPicker();

        Assert.Equal("Halloween", Highlighted(cut).Children[0].TextContent);
        JSInterop.VerifyFocusAsyncInvoke();
    }

    // keyboard.js moves the focus with the arrow keys, and the highlight follows it.
    [Fact]
    public async Task Focus_SecondRow_HighlightsIt()
    {
        var cut = RenderPicker();

        await Rows(cut)[1].FocusAsync(new FocusEventArgs());

        Assert.Equal("Home movies", Highlighted(cut).Children[0].TextContent);
        Assert.Equal(["-1", "0"], Rows(cut).Select(row => row.GetAttribute("tabindex")));
        Assert.Equal(["false", "true"], Rows(cut).Select(row => row.GetAttribute("aria-selected")));
    }

    [Fact]
    public async Task KeyDownEnter_OnARow_PicksIt()
    {
        var cut = RenderPicker();

        await Rows(cut)[1].KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal([HomeMovies], _picked);
    }

    [Fact]
    public async Task DoubleClick_ARow_PicksIt()
    {
        var cut = RenderPicker();

        await Rows(cut)[1].DoubleClickAsync(new MouseEventArgs());

        Assert.Equal([HomeMovies], _picked);
    }

    [Fact]
    public async Task ClickAddMovie_SecondRowHighlighted_PicksIt()
    {
        var cut = RenderPicker();
        await Rows(cut)[1].FocusAsync(new FocusEventArgs());

        await Button(cut, "Add Movie").ClickAsync(new MouseEventArgs());

        Assert.Equal([HomeMovies], _picked);
    }

    [Fact]
    public async Task ClickCancel_InThePicker_PutsTheCaseBack()
    {
        var cut = RenderPicker();

        await Button(cut, "Cancel").ClickAsync(new MouseEventArgs());

        Assert.True(_cancelled);
        Assert.Empty(_picked);
    }

    // UI-3: Esc cancels, from a row, where the focus starts, or from either button.
    [Fact]
    public async Task KeyDownEscape_OnARow_PutsTheCaseBack()
    {
        var cut = RenderPicker();

        await Rows(cut)[0].KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(_cancelled);
        Assert.Empty(_picked);
    }

    [Fact]
    public async Task KeyDownEscape_OnAButton_PutsTheCaseBack()
    {
        var cut = RenderPicker();

        await Button(cut, "Add Movie").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(_cancelled);
        Assert.Empty(_picked);
    }

    // Past the picker, Checkout's own Esc would ask to cancel the whole sale.
    [Fact]
    public async Task KeyDownEscape_InTheSale_GoesNoFurtherThanThePicker()
    {
        var keysAtTheSale = new List<string>();
        var cut = Render<SaleAround>(parameters => parameters
            .Add(p => p.OnKeyDown, EventCallback.Factory.Create<KeyboardEventArgs>(this, e => keysAtTheSale.Add(e.Key)))
            .AddChildContent<WhichMovie>(picker => picker
                .Add(p => p.Movies, [Halloween, HomeMovies])
                .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => _cancelled = true))));

        await cut.Find(".which-movie tbody tr").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(_cancelled);
        Assert.Empty(keysAtTheSale);
    }

    [Fact]
    public async Task KeyDown_AnyOtherKey_DoesNothing()
    {
        var cut = RenderPicker();

        await Rows(cut)[0].KeyDownAsync(new KeyboardEventArgs { Key = "a" });

        Assert.False(_cancelled);
        Assert.Empty(_picked);
    }

    private IRenderedComponent<WhichMovie> RenderPicker() =>
        Render<WhichMovie>(parameters => parameters
            .Add(p => p.Movies, [Halloween, HomeMovies])
            .Add(p => p.OnPick, EventCallback.Factory.Create<MovieInCase>(this, _picked.Add))
            .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => _cancelled = true)));

    private static IReadOnlyList<IElement> Rows(IRenderedComponent<WhichMovie> cut) =>
        cut.FindAll(".which-movie tbody tr");

    private static IElement Highlighted(IRenderedComponent<WhichMovie> cut) =>
        cut.Find(".which-movie tbody tr.highlighted");

    private static IElement Button(IRenderedComponent<WhichMovie> cut, string text) =>
        cut.FindAll("button").Single(button => button.TextContent == text);

    // Stands in for Checkout around the picker, with an Esc of its own.
    private sealed class SaleAround : ComponentBase
    {
        [Parameter]
        public EventCallback<KeyboardEventArgs> OnKeyDown { get; set; }

        [Parameter]
        public RenderFragment? ChildContent { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "onkeydown", OnKeyDown);
            builder.AddContent(2, ChildContent);
            builder.CloseElement();
        }
    }
}
