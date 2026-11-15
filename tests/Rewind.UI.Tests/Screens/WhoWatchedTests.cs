using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Rewind.Core.Members;
using Rewind.Core.Returns;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class WhoWatchedTests : BunitContext
{
    // The screen's order runs against both name and id, so a form that sorted the people would show it. No id
    // is a row's number either, so a watcher sent out with their row would show too.
    private static readonly MemberRow Mom = new(12, "Mom", "MEM-003", IsActive: true);
    private static readonly MemberRow Dad = new(7, "Dad", "MEM-002", IsActive: true);
    private static readonly MemberRow Ava = new(3, "Ava", "MEM-001", IsActive: true);
    private static readonly IReadOnlyList<MemberRow> Family = [Mom, Dad, Ava];

    private readonly List<IReadOnlyList<Watcher>> _saved = [];
    private bool _skipped;

    // Each name labels its own list, in the order the screen gives them.
    [Fact]
    public void Render_People_ListsEachByNameInTheOrderGiven()
    {
        var cut = RenderForm();

        Assert.Equal(["Mom:", "Dad:", "Ava:"], Labels(cut).Select(label => label.TextContent));
        Assert.Equal(Lists(cut).Select(list => list.Id), Labels(cut).Select(label => label.GetAttribute("for")));
    }

    // Nobody is chosen for the clerk, the renter included: the watch log says only what the clerk said.
    [Fact]
    public void Render_People_StartsEveryoneOnDidntWatch()
    {
        var cut = RenderForm();

        Assert.Equal(["Didn't watch", "Didn't watch", "Didn't watch"], Lists(cut).Select(Chosen));
    }

    // Words, not star characters, which the pixel font may not have. Each choice starts with a character of
    // its own, the key that picks it, so the browser's own type-ahead picks the same choice as the form, or none.
    [Fact]
    public void Render_EachList_OffersSevenChoicesEachStartingWithItsOwnCharacter()
    {
        var cut = RenderForm();

        var choices = Lists(cut).Select(list => list.QuerySelectorAll("option").Select(option => option.TextContent).ToList()).ToList();

        Assert.All(choices, offered => Assert.Equal(["Didn't watch", "Watched, no stars", "1 star", "2 stars", "3 stars", "4 stars", "5 stars"], offered));
        Assert.Distinct(choices[0].Select(choice => char.ToUpperInvariant(choice[0])));
    }

    // UI-3: the clerk can pick from the first list at once, and a redraw leaves the focus where the clerk put it.
    [Fact]
    public async Task Render_Opens_FocusesTheFirstListOnce()
    {
        var cut = RenderForm();
        var firstList = Lists(cut)[0].GetAttribute("blazor:elementreference"); // bUnit writes it on the first render only

        await PickAsync(cut, Dad, "3 stars");

        var focused = Assert.IsType<ElementReference>(JSInterop.VerifyFocusAsyncInvoke().Arguments[0]);
        Assert.Equal(firstList, focused.Id);
    }

    // UI-3: with nobody to ask there's no list, so Save has the focus, and Enter and Esc still work.
    [Fact]
    public void Render_NobodyToAsk_FocusesSave()
    {
        var cut = RenderForm([]);

        var focused = Assert.IsType<ElementReference>(JSInterop.VerifyFocusAsyncInvoke().Arguments[0]);
        Assert.Equal(Button(cut, "Save").GetAttribute("blazor:elementreference"), focused.Id);
    }

    // UI-3: Tab follows the visual order, down the lists, then to Save, then to Skip.
    [Fact]
    public void Render_People_TabGoesDownTheListsThenSaveThenSkip()
    {
        var cut = RenderForm();

        var stops = cut.FindAll("select, button").Select(control => control.LocalName == "select" ? NameOn(cut, control) : control.TextContent);

        Assert.Equal(["Mom:", "Dad:", "Ava:", "Save", "Skip"], stops);
        Assert.Empty(cut.FindAll("[tabindex]"));
    }

    // UI-3: Save is the default button: the form's one submit button, which Enter in a list presses, drawn
    // with the dark border of a Windows 98 dialog's default.
    [Fact]
    public void Render_Buttons_SaveIsTheDefault()
    {
        var cut = RenderForm();

        var submit = Assert.Single(cut.FindAll("button[type=submit]"));

        Assert.Equal("Save", submit.TextContent);
        Assert.Contains("default", submit.ClassList);
    }

    // The screen redraws the form while it's open, and the clerk's picks stay.
    [Fact]
    public async Task Render_ScreenRedraws_KeepsWhatWasPicked()
    {
        var cut = RenderForm();
        await PickAsync(cut, Dad, "2 stars");

        cut.Render(parameters => parameters.Add(p => p.People, Family));

        Assert.Equal(["Didn't watch", "2 stars", "Didn't watch"], Lists(cut).Select(Chosen));
    }

    // Core logs each watcher by their member's id, with the stars picked, here at both ends. They go out in the
    // screen's order, whichever was picked first.
    [Fact]
    public async Task ClickSave_StarsPicked_SavesThemWithEachMembersId()
    {
        var cut = RenderForm();
        await PickAsync(cut, Ava, "1 star");
        await PickAsync(cut, Mom, "5 stars");

        await Button(cut, "Save").ClickAsync(new MouseEventArgs());

        Assert.Equal([new Watcher(Mom.Id, 5), new Watcher(Ava.Id, 1)], Assert.Single(_saved));
        Assert.False(_skipped);
    }

    // DATA-3: watched with no stars is null stars, never 0.
    [Fact]
    public async Task ClickSave_WatchedNoStars_SavesNullStars()
    {
        var cut = RenderForm();
        await PickAsync(cut, Dad, "Watched, no stars");

        await Button(cut, "Save").ClickAsync(new MouseEventArgs());

        Assert.Equal([new Watcher(Dad.Id, null)], Assert.Single(_saved));
    }

    // Only who watched goes in the watch log, so anyone on Didn't watch is left out, put back on it or not.
    [Fact]
    public async Task ClickSave_SomeoneOnDidntWatch_LeavesThemOut()
    {
        var cut = RenderForm();
        await PickAsync(cut, Mom, "3 stars");
        await PickAsync(cut, Mom, "Didn't watch");
        await PickAsync(cut, Dad, "4 stars");

        await Button(cut, "Save").ClickAsync(new MouseEventArgs());

        Assert.Equal([new Watcher(Dad.Id, 4)], Assert.Single(_saved));
    }

    // ADR-0012: a skip saves nothing, so nobody chosen is one, rather than an empty list for Core.
    [Fact]
    public async Task ClickSave_NobodyChosen_SkipsAndSavesNothing()
    {
        var cut = RenderForm();

        await Button(cut, "Save").ClickAsync(new MouseEventArgs());

        Assert.True(_skipped);
        Assert.Empty(_saved);
    }

    // keyboard.js has Enter in a drop-down list submit the list's own form, so the lists are in the one Save
    // submits.
    [Fact]
    public async Task Submit_FromAList_SavesWhatWasPicked()
    {
        var cut = RenderForm();
        await PickAsync(cut, Ava, "4 stars");
        var form = Lists(cut)[2].Closest("form");
        Assert.NotNull(form);

        await form.SubmitAsync();

        Assert.Equal([new Watcher(Ava.Id, 4)], Assert.Single(_saved));
    }

    [Fact]
    public async Task ClickSkip_StarsPicked_SkipsAndSavesNothing()
    {
        var cut = RenderForm();
        await PickAsync(cut, Mom, "5 stars");

        await Button(cut, "Skip").ClickAsync(new MouseEventArgs());

        Assert.True(_skipped);
        Assert.Empty(_saved);
    }

    // UI-3: Esc skips, from any list or button.
    [Fact]
    public async Task KeyDownEscape_OnAList_SkipsAndSavesNothing()
    {
        var cut = RenderForm();
        await PickAsync(cut, Ava, "2 stars");

        await Lists(cut)[2].KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(_skipped);
        Assert.Empty(_saved);
    }

    [Fact]
    public async Task KeyDownEscape_OnAButton_Skips()
    {
        var cut = RenderForm();

        await Button(cut, "Save").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(_skipped);
        Assert.Empty(_saved);
    }

    // Esc stops at the form, as Which movie?'s does, so the screen's own Esc never hears it.
    [Fact]
    public async Task KeyDownEscape_InTheScreen_GoesNoFurtherThanTheForm()
    {
        var keysAtTheScreen = new List<string>();
        var cut = Render<ScreenAround>(parameters => parameters
            .Add(p => p.OnKeyDown, EventCallback.Factory.Create<KeyboardEventArgs>(this, e => keysAtTheScreen.Add(e.Key)))
            .AddChildContent<WhoWatched>(form => form
                .Add(p => p.People, Family)
                .Add(p => p.OnSkip, EventCallback.Factory.Create(this, () => _skipped = true))));

        await cut.Find(".who-watched select").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(_skipped);
        Assert.Empty(keysAtTheScreen);
    }

    // UI-3: a key picks the choice that starts with it, at once, for the person whose list it's typed in.
    [Fact]
    public async Task KeyDown_FiveInAList_PicksFiveStarsForThatPersonAlone()
    {
        var cut = RenderForm();

        await TypeAsync(cut, Dad, "5");

        Assert.Equal(["Didn't watch", "5 stars", "Didn't watch"], Lists(cut).Select(Chosen));
    }

    // The browser's own type-ahead joins keys typed within a second, so 4 then 5 would look for "45" and stay on
    // 4 stars. The form picks by each key, so the last one wins, and Save sends what the list shows. bUnit has no
    // type-ahead, so this pins the form's half.
    [Fact]
    public async Task KeyDown_FourThenFive_EndsOnFiveStarsAndSavesFive()
    {
        var cut = RenderForm();
        await TypeAsync(cut, Dad, "4");

        await TypeAsync(cut, Dad, "5");
        await Button(cut, "Save").ClickAsync(new MouseEventArgs());

        Assert.Equal("5 stars", Chosen(ListOf(cut, Dad)));
        Assert.Equal([new Watcher(Dad.Id, 5)], Assert.Single(_saved));
    }

    // A key picks by its choice's words, compared without case, as typing in a list ignores it.
    [Theory]
    [InlineData("W")]
    [InlineData("w")]
    public async Task KeyDown_WInEitherCase_PicksWatchedNoStars(string key)
    {
        var cut = RenderForm();

        await TypeAsync(cut, Dad, key);

        Assert.Equal("Watched, no stars", Chosen(ListOf(cut, Dad)));
    }

    [Theory]
    [InlineData("D")]
    [InlineData("d")]
    public async Task KeyDown_DInEitherCase_PutsThemBackOnDidntWatch(string key)
    {
        var cut = RenderForm();
        await PickAsync(cut, Dad, "3 stars");

        await TypeAsync(cut, Dad, key);

        Assert.Equal("Didn't watch", Chosen(ListOf(cut, Dad)));
    }

    // Only a choice's first character picks it, so any other character leaves the pick alone.
    [Theory]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("6")]
    public async Task KeyDown_CharacterThatStartsNoChoice_ChangesNothing(string key)
    {
        var cut = RenderForm();
        await PickAsync(cut, Dad, "3 stars");

        await TypeAsync(cut, Dad, key);

        Assert.Equal("3 stars", Chosen(ListOf(cut, Dad)));
    }

    // Delete starts with D, but it's a named key, not the character D.
    [Fact]
    public async Task KeyDown_Delete_ChangesNothing()
    {
        var cut = RenderForm();
        await PickAsync(cut, Dad, "3 stars");

        await TypeAsync(cut, Dad, "Delete");

        Assert.Equal("3 stars", Chosen(ListOf(cut, Dad)));
    }

    // With Ctrl, Alt or Meta held, a key is a shortcut, not typing.
    [Theory]
    [InlineData("Ctrl")]
    [InlineData("Alt")]
    [InlineData("Meta")]
    public async Task KeyDown_FiveWithAModifierHeld_ChangesNothing(string modifier)
    {
        var cut = RenderForm();

        await ListOf(cut, Dad).KeyDownAsync(new KeyboardEventArgs
        {
            Key = "5",
            CtrlKey = modifier == "Ctrl",
            AltKey = modifier == "Alt",
            MetaKey = modifier == "Meta",
        });

        Assert.Equal("Didn't watch", Chosen(ListOf(cut, Dad)));
    }

    // A key that neither picks nor skips leaves both the picks and the screen alone.
    [Fact]
    public async Task KeyDown_AnyOtherKey_DoesNothing()
    {
        var cut = RenderForm();
        await PickAsync(cut, Mom, "3 stars");

        await TypeAsync(cut, Mom, "a");

        Assert.Equal(["3 stars", "Didn't watch", "Didn't watch"], Lists(cut).Select(Chosen));
        Assert.False(_skipped);
        Assert.Empty(_saved);
    }

    private IRenderedComponent<WhoWatched> RenderForm() => RenderForm(Family);

    private IRenderedComponent<WhoWatched> RenderForm(IReadOnlyList<MemberRow> people) =>
        Render<WhoWatched>(parameters => parameters
            .Add(p => p.People, people)
            .Add(p => p.OnSave, EventCallback.Factory.Create<IReadOnlyList<Watcher>>(this, _saved.Add))
            .Add(p => p.OnSkip, EventCallback.Factory.Create(this, () => _skipped = true)));

    // Picks a choice by its words, from the list beside the person's name, as the clerk does.
    private static Task PickAsync(IRenderedComponent<WhoWatched> cut, MemberRow person, string choice)
    {
        var list = ListOf(cut, person);
        var value = list.QuerySelectorAll("option").Single(option => option.TextContent == choice).GetAttribute("value");
        return list.ChangeAsync(new ChangeEventArgs { Value = value });
    }

    // Types a key in the list beside the person's name. bUnit has no type-ahead, so only the form's own pick runs.
    private static Task TypeAsync(IRenderedComponent<WhoWatched> cut, MemberRow person, string key) =>
        ListOf(cut, person).KeyDownAsync(new KeyboardEventArgs { Key = key });

    // The list beside the person's name, found through the label's for, as a click on the name finds it.
    private static IElement ListOf(IRenderedComponent<WhoWatched> cut, MemberRow person) =>
        cut.Find($"#{Labels(cut).Single(label => label.TextContent == $"{person.Name}:").GetAttribute("for")}");

    // The choice a list shows: the one whose value Blazor gave the list.
    private static string Chosen(IElement list) =>
        list.QuerySelectorAll("option").Single(option => option.GetAttribute("value") == list.GetAttribute("value")).TextContent;

    // The name on the label beside a list.
    private static string NameOn(IRenderedComponent<WhoWatched> cut, IElement list) =>
        cut.Find($"label[for='{list.Id}']").TextContent;

    private static IReadOnlyList<IElement> Labels(IRenderedComponent<WhoWatched> cut) =>
        cut.FindAll(".who-watched label");

    private static IReadOnlyList<IElement> Lists(IRenderedComponent<WhoWatched> cut) =>
        cut.FindAll(".who-watched select");

    private static IElement Button(IRenderedComponent<WhoWatched> cut, string text) =>
        cut.FindAll("button").Single(button => button.TextContent == text);

    // Stands in for the Return screen around the form, with an Esc of its own.
    private sealed class ScreenAround : ComponentBase
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
