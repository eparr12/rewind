using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Rewind.Core.Shelves;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

// The form asks Core nothing and shows no dialog, so these tests give it no services: one it asked for would fail the
// render.
public sealed class MovieShelvesTests : BunitContext
{
    private const string Nightmare = "The Nightmare Before Christmas";

    // Core's order: the seasonal shelves by name, then each member's Staff Picks by member. No Id is in that order, so
    // a form that sorted the shelves, or the Ids it hands back, would show it.
    private static readonly MovieShelfRow Christmas = new(9, "Christmas", new Season(12, 1, 12, 25), HasTheMovie: false);
    private static readonly MovieShelfRow Halloween = new(4, "Halloween", new Season(10, 1, 10, 31), HasTheMovie: true);
    private static readonly MovieShelfRow DadsPicks = new(2, "Dad's Staff Picks", Season: null, HasTheMovie: true);
    private static readonly MovieShelfRow MomsPicks = new(7, "Mom's Staff Picks", Season: null, HasTheMovie: false);
    private static readonly MovieShelfRow[] Shelves = [Christmas, Halloween, DadsPicks, MomsPicks];

    private readonly List<IReadOnlyList<int>> _saved = [];
    private int _cancels;

    // Remakes share titles, so the year follows the title, as everywhere a screen names a movie.
    [Theory]
    [InlineData(1993, "Shelves for The Nightmare Before Christmas (1993)")]
    [InlineData(null, "Shelves for The Nightmare Before Christmas")] // DATA-3: an unknown year is left out, never 0
    public void Render_Movie_NamesTheGroupBoxForIt(int? year, string name)
    {
        var cut = RenderForm(Shelves, year: year);

        Assert.Equal(name, cut.Find("fieldset.movie-shelves > legend").TextContent);
    }

    // The form lists each seasonal shelf in season or not, so each says when it shows up, as F5's Shelves tab writes
    // it. A member's Staff Picks has no season, so it's the name alone.
    [Fact]
    public void Render_Shelves_ListsEachInTheOrderGivenWithItsSeason()
    {
        var cut = RenderForm();

        Assert.Equal(
            ["Christmas (December 1 to December 25)", "Halloween (October 1 to October 31)", "Dad's Staff Picks", "Mom's Staff Picks"],
            Labels(cut).Select(label => label.TextContent));
    }

    // 98.css hides each checkbox and draws its box on the label right after it, which finds the checkbox by its for, as
    // a click on the words does.
    [Fact]
    public void Render_EachCheckbox_HasItsOwnLabelRightAfterIt()
    {
        var cut = RenderForm();

        var checkboxes = Checkboxes(cut);

        Assert.Equal(Shelves.Length, checkboxes.Count);
        Assert.Distinct(checkboxes.Select(checkbox => checkbox.Id));
        Assert.All(checkboxes, checkbox =>
        {
            var next = checkbox.NextElementSibling;
            Assert.NotNull(next);
            Assert.Equal(("label", checkbox.Id), (next.LocalName, next.GetAttribute("for")));
        });
    }

    [Fact]
    public void Render_Shelves_TicksTheOnesTheMovieSitsOn()
    {
        var cut = RenderForm();

        Assert.Equal([false, true, true, false], Checkboxes(cut).Select(IsTicked));
    }

    // UI-3: the clerk can tick the first shelf at once, and a redraw leaves the focus where the clerk put it.
    [Fact]
    public async Task Render_Opens_FocusesTheFirstCheckboxOnce()
    {
        var cut = RenderForm();
        var first = Checkboxes(cut)[0].GetAttribute("blazor:elementreference"); // bUnit writes it on the first render only

        await TickAsync(cut, MomsPicks, ticked: true);

        var focused = Assert.IsType<ElementReference>(JSInterop.VerifyFocusAsyncInvoke().Arguments[0]);
        Assert.Equal(first, focused.Id);
    }

    // UI-3: Tab follows the visual order, down the checkboxes, then to Save, then to Cancel.
    [Fact]
    public void Render_Shelves_TabGoesDownTheCheckboxesThenSaveThenCancel()
    {
        var cut = RenderForm();

        var stops = cut.FindAll("input, button").Select(control => control.LocalName == "input" ? WordsBeside(cut, control) : control.TextContent);

        Assert.Equal(
            ["Christmas (December 1 to December 25)", "Halloween (October 1 to October 31)", "Dad's Staff Picks", "Mom's Staff Picks", "Save", "Cancel"],
            stops);
        Assert.Empty(cut.FindAll("[tabindex]"));
    }

    // UI-3: Save is the default button: the form's one submit button, which WebView2 presses for Enter in a checkbox,
    // drawn with the dark border of a Windows 98 dialog's default.
    [Fact]
    public void Render_Buttons_SaveIsTheDefault()
    {
        var cut = RenderForm();

        var submit = Assert.Single(cut.FindAll("button[type=submit]"));

        Assert.Equal("Save", submit.TextContent);
        Assert.Contains("default", submit.ClassList);
    }

    // The screen redraws the form while it's open, and the clerk's ticks stay.
    [Fact]
    public async Task Render_ScreenRedraws_KeepsWhatWasTicked()
    {
        var cut = RenderForm();
        await TickAsync(cut, Christmas, ticked: true);
        await TickAsync(cut, DadsPicks, ticked: false);

        cut.Render(parameters => parameters.Add(p => p.Shelves, Shelves));

        Assert.Equal([true, true, false, false], Checkboxes(cut).Select(IsTicked));
    }

    // Core works out what changed, so Save hands back every shelf that's ticked, changed or not, in the order shown.
    // Enter in a checkbox presses Save as Enter on Save does, which WebView2 does for the form.
    [Theory]
    [InlineData("Save", "nothing", new[] { 4, 2 })]
    [InlineData("Save", "a tick", new[] { 9, 4, 2 })]
    [InlineData("Save", "an untick", new[] { 2 })]
    [InlineData("Enter in a checkbox", "nothing", new[] { 4, 2 })]
    [InlineData("Enter in a checkbox", "a tick", new[] { 9, 4, 2 })]
    [InlineData("Enter in a checkbox", "an untick", new[] { 2 })]
    [InlineData("Enter on a button", "nothing", new[] { 4, 2 })]
    [InlineData("Enter on a button", "a tick", new[] { 9, 4, 2 })]
    [InlineData("Enter on a button", "an untick", new[] { 2 })]
    public async Task Save_AfterAChangeOrNone_HandsBackEveryTickedShelf(string way, string change, int[] ticked)
    {
        var cut = RenderForm();
        await ChangeAsync(cut, change);

        await SaveAsync(cut, way);

        Assert.Equal(ticked, Assert.Single(_saved));
        Assert.Equal(0, _cancels);
    }

    // Unlike Who watched?, where nobody chosen is a skip, nothing ticked is a save: it takes the movie off every shelf.
    [Fact]
    public async Task ClickSave_EverythingUnticked_HandsBackNoShelves()
    {
        var cut = RenderForm();
        await TickAsync(cut, Halloween, ticked: false);
        await TickAsync(cut, DadsPicks, ticked: false);

        await Button(cut, "Save").ClickAsync(new MouseEventArgs());

        Assert.Empty(Assert.Single(_saved));
        Assert.Equal(0, _cancels);
    }

    // UI-3: Cancel and Esc close the form with nothing to save, whatever was ticked. Enter on Cancel presses it, as Enter
    // presses the focused button.
    [Theory]
    [InlineData("Cancel")]
    [InlineData("Enter on Cancel")]
    [InlineData("Esc in a checkbox")]
    [InlineData("Esc on a button")]
    public async Task Cancel_AfterATick_HandsBackNothing(string way)
    {
        var cut = RenderForm();
        await TickAsync(cut, Christmas, ticked: true);

        await CloseAsync(cut, way);

        Assert.Equal(1, _cancels);
        Assert.Empty(_saved);
    }

    // Esc stops at the form, as Who watched?'s does, so the screen's own Esc never hears it, with shelves or without.
    [Theory]
    [InlineData("in a checkbox")]
    [InlineData("on OK, with no shelves")]
    public async Task KeyDownEscape_InTheScreen_GoesNoFurtherThanTheForm(string where)
    {
        var withShelves = where == "in a checkbox";
        var keysAtTheScreen = new List<string>();
        var cut = Render<ScreenAround>(parameters => parameters
            .Add(p => p.OnKeyDown, EventCallback.Factory.Create<KeyboardEventArgs>(this, e => keysAtTheScreen.Add(e.Key)))
            .AddChildContent<MovieShelves>(form => form
                .Add(p => p.Title, Nightmare)
                .Add(p => p.Shelves, withShelves ? Shelves : [])
                .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => _cancels++))));

        await cut.Find(withShelves ? ".movie-shelves input" : ".movie-shelves button").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(1, _cancels);
        Assert.Empty(keysAtTheScreen);
    }

    // A key that neither saves nor cancels leaves the ticks alone. Space ticks a box in WebView2, which bUnit has no part
    // of, so here it's a key like any other.
    [Theory]
    [InlineData("a")]
    [InlineData(" ")]
    public async Task KeyDown_AnyOtherKey_DoesNothing(string key)
    {
        var cut = RenderForm();

        await Checkboxes(cut)[0].KeyDownAsync(new KeyboardEventArgs { Key = key });

        Assert.Equal([false, true, true, false], Checkboxes(cut).Select(IsTicked));
        Assert.Equal(0, _cancels);
        Assert.Empty(_saved);
    }

    // UI-6: with no shelves at all there's nothing to tick, so the form says where shelves come from, with OK alone.
    [Fact]
    public void Render_NoShelves_SaysWhereShelvesComeFrom()
    {
        var cut = RenderForm([]);

        Assert.Equal("No shelves yet. The Store Manager adds seasonal shelves at F5.", cut.Find(".movie-shelves p").TextContent);
        Assert.Empty(Checkboxes(cut));
        Assert.Equal(["OK"], cut.FindAll("button").Select(button => button.TextContent));
    }

    // UI-3: with no checkbox, OK, the default button, has the focus, as in a Windows 98 dialog.
    [Fact]
    public void Render_NoShelves_FocusesOk()
    {
        var cut = RenderForm([]);

        var ok = Button(cut, "OK");
        var focused = Assert.IsType<ElementReference>(JSInterop.VerifyFocusAsyncInvoke().Arguments[0]);

        Assert.Equal(ok.GetAttribute("blazor:elementreference"), focused.Id);
        Assert.Contains("default", ok.ClassList);
    }

    // Enter, which presses OK, and Esc both close it, with nothing to save.
    [Theory]
    [InlineData("OK")]
    [InlineData("Enter on OK")]
    [InlineData("Esc on a button")]
    public async Task Close_NoShelves_HandsBackNothing(string way)
    {
        var cut = RenderForm([]);

        await CloseAsync(cut, way);

        Assert.Equal(1, _cancels);
        Assert.Empty(_saved);
    }

    // UI-10: a name typed on F5, a shelf's or a member's, renders as text, so markup in it shows as typed and draws
    // nothing.
    [Fact]
    public void Render_ShelfNamesWithMarkup_ShowThemAsTyped()
    {
        const string Typed = "<i>Halloween</i> & <b>Friends</b>";
        var cut = RenderForm([Halloween with { Name = Typed }, DadsPicks with { Name = $"{Typed}'s Staff Picks" }]);

        Assert.Equal([$"{Typed} (October 1 to October 31)", $"{Typed}'s Staff Picks"], Labels(cut).Select(label => label.TextContent));
        Assert.Empty(cut.FindAll("i, b"));
    }

    // UI-10: so does a title from TMDB.
    [Fact]
    public void Render_TitleWithMarkup_ShowsItAsTyped()
    {
        const string Typed = "<i>Nightmare</i> & <b>Friends</b>";
        var cut = RenderForm(Shelves, title: Typed);

        Assert.Equal($"Shelves for {Typed} (1993)", cut.Find("legend").TextContent);
        Assert.Empty(cut.FindAll("i, b"));
    }

    private IRenderedComponent<MovieShelves> RenderForm() => RenderForm(Shelves);

    private IRenderedComponent<MovieShelves> RenderForm(IReadOnlyList<MovieShelfRow> shelves, string title = Nightmare, int? year = 1993) =>
        Render<MovieShelves>(parameters => parameters
            .Add(p => p.Title, title)
            .Add(p => p.Year, year)
            .Add(p => p.Shelves, shelves)
            .Add(p => p.OnSave, EventCallback.Factory.Create<IReadOnlyList<int>>(this, _saved.Add))
            .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => _cancels++)));

    private static Task ChangeAsync(IRenderedComponent<MovieShelves> cut, string change) => change switch
    {
        "nothing" => Task.CompletedTask,
        "a tick" => TickAsync(cut, Christmas, ticked: true),
        "an untick" => TickAsync(cut, Halloween, ticked: false),
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Not a change a test makes."),
    };

    // Ticks or unticks a shelf's checkbox, as Space or a click does in WebView2: the box changes, and Blazor hears it.
    private static Task TickAsync(IRenderedComponent<MovieShelves> cut, MovieShelfRow shelf, bool ticked) =>
        Checkboxes(cut)[Array.IndexOf(Shelves, shelf)].ChangeAsync(new ChangeEventArgs { Value = ticked });

    private static Task SaveAsync(IRenderedComponent<MovieShelves> cut, string way) => way switch
    {
        "Save" => Button(cut, "Save").ClickAsync(new MouseEventArgs()),
        "Enter in a checkbox" => PressEnterAsync(() => Checkboxes(cut)[^1]),
        "Enter on a button" => PressEnterAsync(() => Button(cut, "Save")),
        _ => throw new ArgumentOutOfRangeException(nameof(way), way, "Not a way to save."),
    };

    private static Task CloseAsync(IRenderedComponent<MovieShelves> cut, string way) => way switch
    {
        "Cancel" or "OK" => Button(cut, way).ClickAsync(new MouseEventArgs()),
        "Enter on Cancel" => PressEnterAsync(() => Button(cut, "Cancel")),
        "Enter on OK" => PressEnterAsync(() => Button(cut, "OK")),
        "Esc in a checkbox" => Checkboxes(cut)[0].KeyDownAsync(new KeyboardEventArgs { Key = "Escape" }),
        "Esc on a button" => cut.Find(".movie-shelves button").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" }),
        _ => throw new ArgumentOutOfRangeException(nameof(way), way, "Not a way to close the form."),
    };

    // Enter as WebView2 answers it, which a probe of Edge WebView2 157 showed: the key goes down on the element, where
    // the form's own handlers hear it first, then the browser presses a button: the element itself, when it's a button,
    // or else the first submit button of the element's form, as for Enter in a text box. bUnit does neither part of the
    // browser's, so this does it. The element is found again after the key, which redraws the form.
    private static async Task PressEnterAsync(Func<IElement> find)
    {
        await find().KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        var element = find();
        var pressed = element.LocalName == "button" ? element : element.Closest("form")?.QuerySelector("button:not([type=button])");
        Assert.NotNull(pressed); // Enter in a checkbox presses nothing outside a form with a submit button
        await pressed.ClickAsync(new MouseEventArgs());
    }

    private static bool IsTicked(IElement checkbox) => checkbox.HasAttribute("checked");

    // The words on the label beside a checkbox, which 98.css draws its box on.
    private static string WordsBeside(IRenderedComponent<MovieShelves> cut, IElement checkbox) =>
        cut.Find($"label[for='{checkbox.Id}']").TextContent;

    private static IReadOnlyList<IElement> Labels(IRenderedComponent<MovieShelves> cut) =>
        cut.FindAll(".movie-shelves label");

    private static IReadOnlyList<IElement> Checkboxes(IRenderedComponent<MovieShelves> cut) =>
        cut.FindAll(".movie-shelves input[type=checkbox]");

    private static IElement Button(IRenderedComponent<MovieShelves> cut, string text) =>
        cut.FindAll("button").Single(button => button.TextContent == text);
}
