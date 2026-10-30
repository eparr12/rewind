using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Rewind.Core.Concessions;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class SnacksTabTests : BunitContext
{
    private const string Prompt = "Type the new snack's name and price, then press Enter. Check its code first: it can't change once the snack is added.";
    private const string NotAPrice = "That isn't a price. Type it in dollars, from 0.00 to 99.99, such as 1.00.";

    private static readonly ConcessionRow Popcorn = new(1, "Popcorn", "CON-POPCORN", 100, IsActive: true);
    private static readonly ConcessionRow Candy = new(2, "Candy", "CON-CANDY", 50, IsActive: false);

    private readonly FakeConcessionBar _bar = new();

    public SnacksTabTests() => Services.AddSingleton<IConcessionBar>(_bar);

    [Fact]
    public void Render_Snacks_ListsEachWithItsCodeAndPrice()
    {
        _bar.Rows.AddRange([Candy, Popcorn]);

        var cut = Render<SnacksTab>();

        Assert.Equal([["Candy", "CON-CANDY", "$0.50", "No"], ["Popcorn", "CON-POPCORN", "$1.00", "Yes"]], Rows(cut));
    }

    // UI-6: an empty list says so, rather than showing an empty table.
    [Fact]
    public void Render_NoSnacks_SaysSo()
    {
        var cut = Render<SnacksTab>();

        Assert.Contains(cut.FindAll("p"), p => p.TextContent == "No snacks yet.");
        Assert.Empty(cut.FindAll("table"));
    }

    // UI-6: the tab says it's reading, rather than showing a form whose snack would have no list to join.
    [Fact]
    public void Render_WhileReading_SaysSo()
    {
        _bar.Listing = new TaskCompletionSource<IReadOnlyList<ConcessionRow>>().Task;

        var cut = Render<SnacksTab>();

        Assert.Contains(cut.FindAll("p"), p => p.TextContent == "Reading the snacks...");
        Assert.Empty(cut.FindAll("form"));
    }

    // UI-6: one line always says what to do.
    [Fact]
    public void Render_SnacksRead_SaysWhatToDo()
    {
        var cut = Render<SnacksTab>();

        Assert.Equal(Prompt, Line(cut));
    }

    // UI-3: the Store Manager can type the first snack's name as soon as the tab opens.
    [Fact]
    public void Render_SnacksRead_FocusesTheNameBox()
    {
        var cut = Render<SnacksTab>();

        AssertFocusedLast(BoxId(cut, "#snacks-name"), focuses: 1);
    }

    // The code box starts with what every code starts with, for the name to finish.
    [Fact]
    public void Render_SnacksRead_StartsTheCodeWithItsPrefix()
    {
        var cut = Render<SnacksTab>();

        Assert.Equal("CON-", Value(cut, "#snacks-code"));
    }

    // Core's limits are its backstop, so each box stops there first: a name at ConcessionItem.MaxNameLength
    // and a code at ConcessionCodes.MaxLength.
    [Fact]
    public void Render_SnacksRead_StopsTheBoxesAtCoresLimits()
    {
        var cut = Render<SnacksTab>();

        Assert.Equal(("30", "24"), (MaxLength(cut, "#snacks-name"), MaxLength(cut, "#snacks-code")));
    }

    // The code is made from the name as it's typed, as ConcessionCodes suggests it.
    [Fact]
    public async Task Input_Name_SuggestsTheCode()
    {
        var cut = Render<SnacksTab>();

        await cut.Find("#snacks-name").InputAsync("Kettle corn");

        Assert.Equal("CON-KETTLECORN", Value(cut, "#snacks-code"));
    }

    // Each change to the name changes the code, while the code is still the one suggested.
    [Fact]
    public async Task Input_NameTypedOn_SuggestsTheCodeAgain()
    {
        var cut = Render<SnacksTab>();
        await cut.Find("#snacks-name").InputAsync("Kettle");

        await cut.Find("#snacks-name").InputAsync("Kettle corn");

        Assert.Equal("CON-KETTLECORN", Value(cut, "#snacks-code"));
    }

    // A code the Store Manager typed is theirs, so a change to the name leaves it as it is.
    [Fact]
    public async Task Input_NameAfterACodeWasTyped_KeepsTheCode()
    {
        var cut = Render<SnacksTab>();
        await cut.Find("#snacks-name").InputAsync("Fountain pop");
        await cut.Find("#snacks-code").InputAsync("CON-POP");

        await cut.Find("#snacks-name").InputAsync("Fountain pop or SodaStream");

        Assert.Equal("CON-POP", Value(cut, "#snacks-code"));
    }

    // Emptying the code box hands the code back to the name, so the suggestion comes back without the Store
    // Manager having to know it.
    [Fact]
    public async Task Input_NameAfterTheCodeWasEmptied_SuggestsTheCodeAgain()
    {
        var cut = Render<SnacksTab>();
        await cut.Find("#snacks-name").InputAsync("Fountain pop");
        await cut.Find("#snacks-code").InputAsync("CON-POP");
        await cut.Find("#snacks-code").InputAsync("");

        await cut.Find("#snacks-name").InputAsync("Fountain pop or SodaStream");

        Assert.Equal("CON-FOUNTAINPOPORSODASTR", Value(cut, "#snacks-code"));
    }

    // ARCH-5: Core cleans the name and reads the code and the price, so the tab hands them on as typed.
    [Fact]
    public async Task Submit_NewSnack_AddsItAsTyped()
    {
        var cut = Render<SnacksTab>();
        await cut.Find("#snacks-name").InputAsync("Fountain pop or SodaStream");
        await cut.Find("#snacks-code").InputAsync("con-pop");
        await cut.Find("#snacks-price").InputAsync("$.75");

        await cut.Find("form").SubmitAsync();

        Assert.Equal(["Add Fountain pop or SodaStream con-pop $.75"], _bar.Calls);
    }

    // A snack added here goes at the end, where the Store Manager sees it, rather than among the others.
    [Fact]
    public async Task Submit_NewSnack_AddsItAtTheEnd()
    {
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();
        await TypeSnackAsync(cut, "Candy", "0.5");

        await cut.Find("form").SubmitAsync();

        Assert.Equal([["Popcorn", "CON-POPCORN", "$1.00", "Yes"], ["Candy", "CON-CANDY", "$0.50", "Yes"]], Rows(cut));
    }

    // The line says what Core saved, with the price as Core read it.
    [Fact]
    public async Task Submit_NewSnack_SaysWhatWasAdded()
    {
        var cut = Render<SnacksTab>();
        await TypeSnackAsync(cut, "Candy", "0.5");

        await cut.Find("form").SubmitAsync();

        Assert.Equal("Added Candy at $0.50, with the code CON-CANDY.", Line(cut));
    }

    // So the next snack can be typed straight away, its code following its name again.
    [Fact]
    public async Task Submit_NewSnack_EmptiesTheForm()
    {
        var cut = Render<SnacksTab>();
        await TypeSnackAsync(cut, "Candy", "0.50");
        await cut.Find("#snacks-code").InputAsync("CON-SWEETS");

        await cut.Find("form").SubmitAsync();

        Assert.Equal(("", "CON-", ""), Form(cut));
    }

    // UI-3: the name box has the focus again, for the next snack. A save that waits disables the form, which
    // takes the focus from it.
    [Fact]
    public async Task Submit_NewSnack_FocusesTheNameBox()
    {
        var cut = Render<SnacksTab>();
        var nameBox = BoxId(cut, "#snacks-name");
        await TypeSnackAsync(cut, "Candy", "0.50");

        await cut.Find("form").SubmitAsync();

        AssertFocusedLast(nameBox, focuses: 2);
    }

    // Core decides what counts as a name, so the tab asks it, and says what Core answered: a refusal,
    // worded unlike the prompt, so it's seen to be one.
    [Fact]
    public async Task Submit_NoName_SaysThereIsNoNameYet()
    {
        _bar.Adding = Task.FromResult(new ConcessionChange(ConcessionChangeStatus.NoName, null));
        var cut = Render<SnacksTab>();

        await cut.Find("form").SubmitAsync();

        Assert.Equal("No name yet. Type it, then press Enter.", Line(cut));
    }

    [Fact]
    public async Task Submit_NotACode_SaysWhatACodeIs()
    {
        _bar.Adding = Task.FromResult(new ConcessionChange(ConcessionChangeStatus.NotACode, null));
        var cut = Render<SnacksTab>();

        await cut.Find("form").SubmitAsync();

        Assert.Equal("That isn't a snack code. A code is CON- and 1 to 20 letters from A to Z, such as CON-POPCORN.", Line(cut));
    }

    // 100 typed for a dollar is $100.00, which is over the limit.
    [Fact]
    public async Task Submit_NotAPrice_SaysWhatAPriceIs()
    {
        var cut = Render<SnacksTab>();
        await TypeSnackAsync(cut, "Popcorn", "100");

        await cut.Find("form").SubmitAsync();

        Assert.Equal(NotAPrice, Line(cut));
    }

    // DATA-11: a code stays with its snack for good, so the line says whose it is.
    [Fact]
    public async Task Submit_CodeInUse_SaysWhoseItIs()
    {
        _bar.Adding = Task.FromResult(new ConcessionChange(ConcessionChangeStatus.CodeInUse, Popcorn));
        var cut = Render<SnacksTab>();

        await cut.Find("form").SubmitAsync();

        Assert.Equal("CON-POPCORN is Popcorn's code already. Type a code no snack has yet.", Line(cut));
    }

    // A deactivated snack keeps its code, so it's reactivated rather than added again.
    [Fact]
    public async Task Submit_CodeOfADeactivatedSnack_SaysToReactivateIt()
    {
        _bar.Adding = Task.FromResult(new ConcessionChange(ConcessionChangeStatus.CodeInUse, Candy));
        var cut = Render<SnacksTab>();

        await cut.Find("form").SubmitAsync();

        Assert.Equal(
            "CON-CANDY is Candy's code already, and Candy is deactivated. Reactivate Candy instead, or type a code no snack has yet.",
            Line(cut));
    }

    // What was typed stays, so the slip can be seen and fixed.
    [Fact]
    public async Task Submit_Refused_KeepsWhatWasTyped()
    {
        var cut = Render<SnacksTab>();
        await TypeSnackAsync(cut, "Popcorn", "100");

        await cut.Find("form").SubmitAsync();

        Assert.Equal(("Popcorn", "CON-POPCORN", "100"), Form(cut));
    }

    // UI-3: the box to fix has the focus.
    [Theory]
    [InlineData(ConcessionChangeStatus.NoName, "#snacks-name")]
    [InlineData(ConcessionChangeStatus.NotACode, "#snacks-code")]
    [InlineData(ConcessionChangeStatus.CodeInUse, "#snacks-code")]
    [InlineData(ConcessionChangeStatus.NotAPrice, "#snacks-price")]
    public async Task Submit_Refused_FocusesTheBoxToFix(ConcessionChangeStatus status, string box)
    {
        var owner = status == ConcessionChangeStatus.CodeInUse ? Popcorn : null;
        _bar.Adding = Task.FromResult(new ConcessionChange(status, owner));
        var cut = Render<SnacksTab>();
        var boxId = BoxId(cut, box);

        await cut.Find("form").SubmitAsync();

        AssertFocusedLast(boxId, focuses: 2);
    }

    // While Core saves, nothing can be typed, added or changed.
    [Fact]
    public async Task Submit_WhileSaving_DisablesEveryBoxAndButton()
    {
        var adding = new TaskCompletionSource<ConcessionChange>();
        _bar.Adding = adding.Task;
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();

        var submit = cut.Find("form").SubmitAsync();

        Assert.All(cut.FindAll("input, button"), control => Assert.True(control.HasAttribute("disabled")));
        adding.SetResult(new ConcessionChange(ConcessionChangeStatus.NoName, null));
        await submit;
    }

    // A second Enter while Core saves would add the snack twice.
    [Fact]
    public async Task Submit_TwiceWhileSaving_AddsOnce()
    {
        var adding = new TaskCompletionSource<ConcessionChange>();
        _bar.Adding = adding.Task;
        var cut = Render<SnacksTab>();
        var form = cut.Find("form");

        var first = form.SubmitAsync();
        var second = form.SubmitAsync();
        adding.SetResult(new ConcessionChange(ConcessionChangeStatus.NoName, null));
        await Task.WhenAll(first, second);

        Assert.Single(_bar.Calls);
    }

    // UI-6: the tab doesn't catch a save that fails. A form handler's exception goes on to the renderer,
    // which hands it to the screen's ErrorBoundary; rendered alone here, it comes back out of the submit.
    [Fact]
    public async Task Submit_SaveFails_LeavesItToTheErrorBoundary()
    {
        _bar.Adding = Task.FromException<ConcessionChange>(new InvalidOperationException("The save failed."));
        var cut = Render<SnacksTab>();

        var submit = () => cut.Find("form").SubmitAsync();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(submit);
        Assert.Equal("The save failed.", thrown.Message);
    }

    [Fact]
    public async Task Rename_Clicked_PutsTheNameInABox()
    {
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();

        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());

        Assert.Equal("Popcorn", Value(cut, ".snacks-rename"));
    }

    // As for a new snack's name.
    [Fact]
    public async Task Rename_Clicked_StopsTheBoxAtCoresLimit()
    {
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();

        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());

        Assert.Equal("30", MaxLength(cut, ".snacks-rename"));
    }

    // Renaming is for typos (Build Plan 3.1). UI-3: Enter saves the name, as it was typed.
    [Fact]
    public async Task Rename_EnterPressed_SavesTheNewName()
    {
        _bar.Rows.Add(Popcorn with { Name = "Popcron" });
        var cut = Render<SnacksTab>();
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());
        await cut.Find(".snacks-rename").InputAsync("Popcorn");

        await cut.Find(".snacks-rename").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(["Rename 1 Popcorn"], _bar.Calls);
        Assert.Equal([["Popcorn", "CON-POPCORN", "$1.00", "Yes"]], Rows(cut));
    }

    // UI-3: Esc cancels, and the name stays as it was.
    [Fact]
    public async Task Rename_EscPressed_KeepsTheName()
    {
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());
        await cut.Find(".snacks-rename").InputAsync("Kettle corn");

        await cut.Find(".snacks-rename").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(_bar.Calls);
        Assert.Equal([["Popcorn", "CON-POPCORN", "$1.00", "Yes"]], Rows(cut));
    }

    // The box stays open, so the name can be typed again.
    [Fact]
    public async Task Rename_BlankName_SaysASnackNeedsOne()
    {
        _bar.Rows.Add(Popcorn);
        _bar.Renaming = Task.FromResult(new ConcessionChange(ConcessionChangeStatus.NoName, null));
        var cut = Render<SnacksTab>();
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());
        await cut.Find(".snacks-rename").InputAsync(" ");

        await Button(cut, "Save").ClickAsync(new MouseEventArgs());

        Assert.Equal("A snack needs a name.", Line(cut));
        Assert.Single(cut.FindAll(".snacks-rename"));
    }

    // The box shows the price as Dollars writes it, so a price saved as shown is the same price.
    [Fact]
    public async Task ChangePrice_Clicked_PutsThePriceInABox()
    {
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();

        await Button(cut, "Change price").ClickAsync(new MouseEventArgs());

        Assert.Equal("1.00", Value(cut, ".snacks-reprice"));
    }

    // UI-3: Enter saves the price as it was typed, and the list shows it as Core read it.
    [Fact]
    public async Task ChangePrice_EnterPressed_SavesTheNewPrice()
    {
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();
        await Button(cut, "Change price").ClickAsync(new MouseEventArgs());
        await cut.Find(".snacks-reprice").InputAsync("1.5");

        await cut.Find(".snacks-reprice").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(["SetPrice 1 1.5"], _bar.Calls);
        Assert.Equal([["Popcorn", "CON-POPCORN", "$1.50", "Yes"]], Rows(cut));
    }

    [Fact]
    public async Task ChangePrice_EscPressed_KeepsThePrice()
    {
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();
        await Button(cut, "Change price").ClickAsync(new MouseEventArgs());
        await cut.Find(".snacks-reprice").InputAsync("1.50");

        await cut.Find(".snacks-reprice").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(_bar.Calls);
        Assert.Equal([["Popcorn", "CON-POPCORN", "$1.00", "Yes"]], Rows(cut));
    }

    // The box stays open with what was typed, to fix.
    [Fact]
    public async Task ChangePrice_NotAPrice_SaysWhatAPriceIs()
    {
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();
        await Button(cut, "Change price").ClickAsync(new MouseEventArgs());
        await cut.Find(".snacks-reprice").InputAsync("150");

        await Button(cut, "Save").ClickAsync(new MouseEventArgs());

        Assert.Equal(NotAPrice, Line(cut));
        Assert.Equal("150", Value(cut, ".snacks-reprice"));
    }

    // UI-6: while a name or price is fixed, the form the prompt points to waits, so the line says how to
    // finish, in place of what the last change came to.
    [Theory]
    [InlineData("Rename", "Fix the name, then press Enter. Esc leaves it as it was.")]
    [InlineData("Change price", "Type the new price in dollars, then press Enter. Esc leaves it as it was.")]
    public async Task Edit_Clicked_SaysHowToFinish(string button, string line)
    {
        _bar.Rows.Add(Popcorn);
        _bar.Adding = Task.FromResult(new ConcessionChange(ConcessionChangeStatus.NoName, null));
        var cut = Render<SnacksTab>();
        await cut.Find("form").SubmitAsync();

        await Button(cut, button).ClickAsync(new MouseEventArgs());

        Assert.Equal(line, Line(cut));
    }

    // UI-6: once the snack is left as it was, the line says what to do next, not why the box refused.
    [Fact]
    public async Task Edit_EscPressedAfterARefusal_SaysWhatToDo()
    {
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();
        await Button(cut, "Change price").ClickAsync(new MouseEventArgs());
        await cut.Find(".snacks-reprice").InputAsync("150");
        await cut.Find(".snacks-reprice").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        await cut.Find(".snacks-reprice").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(Prompt, Line(cut));
    }

    // And once it's saved.
    [Fact]
    public async Task Edit_SavedAfterARefusal_SaysWhatToDo()
    {
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();
        await Button(cut, "Change price").ClickAsync(new MouseEventArgs());
        await cut.Find(".snacks-reprice").InputAsync("150");
        await cut.Find(".snacks-reprice").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await cut.Find(".snacks-reprice").InputAsync("1.50");

        await cut.Find(".snacks-reprice").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(Prompt, Line(cut));
    }

    // UI-3: the snack's box has the focus, to type in straight away.
    [Theory]
    [InlineData("Rename", ".snacks-rename")]
    [InlineData("Change price", ".snacks-reprice")]
    public async Task Edit_Clicked_FocusesTheSnacksBox(string button, string box)
    {
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();

        await Button(cut, button).ClickAsync(new MouseEventArgs());

        AssertFocusedLast(BoxId(cut, box), focuses: 2);
    }

    // UI-3: once the snack is saved, or left as it was, the name box has the focus again, for the next snack.
    [Theory]
    [InlineData("Rename", ".snacks-rename", "Enter")]
    [InlineData("Rename", ".snacks-rename", "Escape")]
    [InlineData("Change price", ".snacks-reprice", "Enter")]
    [InlineData("Change price", ".snacks-reprice", "Escape")]
    public async Task Edit_Finished_FocusesTheNameBox(string button, string box, string key)
    {
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();
        var nameBox = BoxId(cut, "#snacks-name");
        await Button(cut, button).ClickAsync(new MouseEventArgs());

        await cut.Find(box).KeyDownAsync(new KeyboardEventArgs { Key = key });

        AssertFocusedLast(nameBox, focuses: 3);
    }

    // UI-3: a refused name or price keeps the focus in its box, to fix. A save that waits disables the box,
    // which takes the focus from it.
    [Theory]
    [InlineData("Rename", ".snacks-rename", " ")]
    [InlineData("Change price", ".snacks-reprice", "150")]
    public async Task Edit_Refused_FocusesTheSnacksBoxAgain(string button, string box, string typed)
    {
        _bar.Rows.Add(Popcorn);
        _bar.Renaming = Task.FromResult(new ConcessionChange(ConcessionChangeStatus.NoName, null));
        var cut = Render<SnacksTab>();
        await Button(cut, button).ClickAsync(new MouseEventArgs());
        var boxId = BoxId(cut, box);
        await cut.Find(box).InputAsync(typed);

        await cut.Find(box).KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        AssertFocusedLast(boxId, focuses: 3);
    }

    // One thing at a time: while a name is fixed, only its box, Save and Cancel can be used.
    [Fact]
    public async Task Rename_Clicked_LocksTheFormAndTheOtherButtons()
    {
        _bar.Rows.AddRange([Popcorn, Candy]);
        var cut = Render<SnacksTab>();

        await cut.FindAll("button").First(button => button.TextContent == "Rename").ClickAsync(new MouseEventArgs());

        var usable = cut.FindAll("input, button")
            .Where(control => !control.HasAttribute("disabled"))
            .Select(control => control.LocalName == "button" ? control.TextContent : control.ClassName);
        Assert.Equal(["snacks-rename", "Save", "Cancel"], usable);
    }

    // The form waits while a name is fixed, so an Enter that reaches it anyway adds nothing.
    [Fact]
    public async Task Submit_WhileANameIsFixed_AddsNothing()
    {
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();
        await TypeSnackAsync(cut, "Candy", "0.50");
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());

        await cut.Find("form").SubmitAsync();

        Assert.Empty(_bar.Calls);
    }

    // As for the form: another snack's button, pressed while a name is fixed, changes nothing.
    [Fact]
    public async Task Reactivate_WhileANameIsFixed_ChangesNothing()
    {
        _bar.Rows.AddRange([Popcorn, Candy]);
        var cut = Render<SnacksTab>();
        await cut.FindAll("button").First(button => button.TextContent == "Rename").ClickAsync(new MouseEventArgs());

        await Button(cut, "Reactivate").ClickAsync(new MouseEventArgs());

        Assert.Empty(_bar.Calls);
    }

    // While Core saves a name or a price, nothing can be typed or pressed, Save and Cancel included.
    [Theory]
    [InlineData("Rename", ".snacks-rename")]
    [InlineData("Change price", ".snacks-reprice")]
    public async Task Edit_WhileSaving_DisablesEveryBoxAndButton(string button, string box)
    {
        var saving = new TaskCompletionSource<ConcessionChange>();
        _bar.Renaming = saving.Task;
        _bar.Repricing = saving.Task;
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();
        await Button(cut, button).ClickAsync(new MouseEventArgs());

        var save = cut.Find(box).KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.All(cut.FindAll("input, button"), control => Assert.True(control.HasAttribute("disabled")));
        saving.SetResult(new ConcessionChange(ConcessionChangeStatus.Saved, Popcorn));
        await save;
    }

    // A second Enter while Core saves the name would save it again in the middle of the first.
    [Fact]
    public async Task Rename_EnterTwiceWhileSaving_SavesOnce()
    {
        var renaming = new TaskCompletionSource<ConcessionChange>();
        _bar.Renaming = renaming.Task;
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());
        var box = cut.Find(".snacks-rename");

        var first = box.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        var second = box.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        renaming.SetResult(new ConcessionChange(ConcessionChangeStatus.NoName, null));
        await Task.WhenAll(first, second);

        Assert.Single(_bar.Calls);
    }

    // DATA-4: deactivated, the snack stays in the list, with a button to bring it back. The other snacks
    // stay as they were.
    [Fact]
    public async Task Deactivate_Clicked_ShowsItInactive()
    {
        _bar.Rows.AddRange([Popcorn, Candy]);
        var cut = Render<SnacksTab>();

        await Button(cut, "Deactivate").ClickAsync(new MouseEventArgs());

        Assert.Equal(["SetActive 1 False"], _bar.Calls);
        Assert.Equal([["Popcorn", "CON-POPCORN", "$1.00", "No"], ["Candy", "CON-CANDY", "$0.50", "No"]], Rows(cut));
        Assert.Equal(2, cut.FindAll("button").Count(button => button.TextContent == "Reactivate"));
    }

    // UI-6: the line says what to do next, not what an earlier change came to.
    [Fact]
    public async Task Deactivate_ClickedAfterARefusal_SaysWhatToDo()
    {
        _bar.Rows.Add(Popcorn);
        _bar.Adding = Task.FromResult(new ConcessionChange(ConcessionChangeStatus.NoName, null));
        var cut = Render<SnacksTab>();
        await cut.Find("form").SubmitAsync();

        await Button(cut, "Deactivate").ClickAsync(new MouseEventArgs());

        Assert.Equal(Prompt, Line(cut));
    }

    [Fact]
    public async Task Reactivate_Clicked_ShowsItActive()
    {
        _bar.Rows.Add(Candy);
        var cut = Render<SnacksTab>();

        await Button(cut, "Reactivate").ClickAsync(new MouseEventArgs());

        Assert.Equal(["SetActive 2 True"], _bar.Calls);
        Assert.Equal([["Candy", "CON-CANDY", "$0.50", "Yes"]], Rows(cut));
    }

    // Leaving the tab stops a read still waiting on the database.
    [Fact]
    public async Task Dispose_WhileReading_CancelsTheRead()
    {
        _bar.Listing = new TaskCompletionSource<IReadOnlyList<ConcessionRow>>().Task;
        _ = Render<SnacksTab>();

        await DisposeComponentsAsync();

        Assert.True(_bar.LastToken.IsCancellationRequested);
    }

    // And a save. Its cancellation raises nothing: ComponentBase ignores a form handler's cancelled task
    // (Microsoft Learn, "ASP.NET Core Blazor synchronization context"), so the tab needs no catch for it.
    [Fact]
    public async Task Dispose_WhileAdding_CancelsTheSaveAndRaisesNothing()
    {
        var adding = new TaskCompletionSource<ConcessionChange>();
        _bar.Adding = adding.Task;
        var cut = Render<SnacksTab>();
        var submit = cut.Find("form").SubmitAsync();
        await DisposeComponentsAsync();

        adding.SetCanceled(_bar.LastToken); // as EF Core ends a save whose token was cancelled
        await submit;

        Assert.True(_bar.LastToken.IsCancellationRequested);
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // Every call waits on the database with the tab's token, so leaving stops whichever is still waiting.
    // Here each has answered already, and the test checks that leaving cancels the token each was given.
    [Fact]
    public async Task Dispose_AfterEachKindOfChange_CancelsEveryCallsToken()
    {
        _bar.Rows.Add(Popcorn);
        var cut = Render<SnacksTab>();
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());
        await cut.Find(".snacks-rename").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await Button(cut, "Change price").ClickAsync(new MouseEventArgs());
        await cut.Find(".snacks-reprice").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await Button(cut, "Deactivate").ClickAsync(new MouseEventArgs());
        await TypeSnackAsync(cut, "Candy", "0.50");
        await cut.Find("form").SubmitAsync();

        await DisposeComponentsAsync();

        Assert.Equal(["Rename 1 Popcorn", "SetPrice 1 1.00", "SetActive 1 False", "Add Candy CON-CANDY 0.50"], _bar.Calls);
        Assert.Equal(5, _bar.Tokens.Count); // the read's, then each change's
        Assert.All(_bar.Tokens, token => Assert.True(token.IsCancellationRequested));
    }

    private static async Task TypeSnackAsync(IRenderedComponent<SnacksTab> cut, string name, string price)
    {
        await cut.Find("#snacks-name").InputAsync(name);
        await cut.Find("#snacks-price").InputAsync(price);
    }

    private static string Line(IRenderedComponent<SnacksTab> cut) => cut.Find(".snacks-prompt").TextContent;

    private static string Value(IRenderedComponent<SnacksTab> cut, string selector) => cut.Find(selector).GetAttribute("value") ?? "";

    private static string? MaxLength(IRenderedComponent<SnacksTab> cut, string selector) => cut.Find(selector).GetAttribute("maxlength");

    // The new snack's name, code and price, as the form's boxes hold them.
    private static (string Name, string Code, string Price) Form(IRenderedComponent<SnacksTab> cut) =>
        (Value(cut, "#snacks-name"), Value(cut, "#snacks-code"), Value(cut, "#snacks-price"));

    private static IElement Button(IRenderedComponent<SnacksTab> cut, string text) =>
        cut.FindAll("button").Single(button => button.TextContent == text);

    // Each snack's name, code, price and whether it's sold, as the list shows them.
    private static List<string[]> Rows(IRenderedComponent<SnacksTab> cut) =>
        [.. cut.FindAll(".snacks-list tbody tr").Select(row => row.QuerySelectorAll("td").Take(4).Select(cell => cell.TextContent).ToArray())];

    // The ID of a box's @ref. bUnit writes it into the markup only in the render that first draws the box,
    // and as "" after that, so a test reads it before anything else happens.
    private static string? BoxId(IRenderedComponent<SnacksTab> cut, string selector) =>
        cut.Find(selector).GetAttribute("blazor:elementreference");

    // UI-3: the element the tab focused last, from its focus calls so far.
    private void AssertFocusedLast(string? elementId, int focuses)
    {
        var focused = Assert.IsType<ElementReference>(JSInterop.VerifyFocusAsyncInvoke(focuses)[^1].Arguments[0]);
        Assert.Equal(elementId, focused.Id);
    }
}
