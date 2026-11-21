using System.Diagnostics;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rewind.Core.Devices;
using Rewind.Core.Scans;
using Rewind.Core.Shelves;
using Rewind.UI.Dialogs;
using Rewind.UI.Errors;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class ShelvesTabTests : BunitContext
{
    private const string Prompt = "Type the new shelf's name, pick its months and type its days, such as October 1 to October 31, then press Enter.";
    private const string NameBox = "#shelves-name";
    private const string FromMonthList = "#shelves-from-month";
    private const string FromDayBox = "#shelves-from-day";
    private const string ToMonthList = "#shelves-to-month";
    private const string ToDayBox = "#shelves-to-day";

    private static readonly KeyboardEventArgs Enter = new() { Key = "Enter" };
    private static readonly KeyboardEventArgs Escape = new() { Key = "Escape" };

    // As Core lists them, by name. On the fake's October 23, Halloween is in season, and Christmas and the deactivated
    // Summer shelf aren't.
    private static readonly SeasonalShelfRow Christmas = new(1, "Christmas", new Season(12, 1, 12, 25), IsActive: true, InSeason: false);
    private static readonly SeasonalShelfRow Halloween = new(2, "Halloween", new Season(10, 1, 10, 31), IsActive: true, InSeason: true);
    private static readonly SeasonalShelfRow Summer = new(3, "Summer", new Season(6, 21, 9, 22), IsActive: false, InSeason: false);

    private readonly FakeDisplayShelves _shelves = new();
    private readonly FakeScanner _scanner = new();
    private readonly DialogService _dialogs = new(new FakeSoundPlayer());
    private readonly ListLogger<ErrorHub> _errorLog = new();
    private bool _crashed;

    public ShelvesTabTests()
    {
        var errors = new ErrorHub(_errorLog);
        errors.Crashed += () => _crashed = true;
        Services.AddSingleton<IDisplayShelves>(_shelves);
        Services.AddSingleton<IScanner>(_scanner);
        Services.AddSingleton(_dialogs);
        Services.AddSingleton(errors);
    }

    // Each shelf in Core's order, with its season in words, whether it's active and whether it's in season tonight,
    // which a deactivated shelf can be too.
    [Fact]
    public void Render_Shelves_ListsEachWithItsSeasonAndWhetherItsActiveAndInSeason()
    {
        _shelves.Rows.AddRange([Christmas, Halloween with { IsActive = false }]);

        var cut = Render<ShelvesTab>();

        Assert.Equal([["Christmas", "December 1 to December 25", "Yes", "No"], ["Halloween", "October 1 to October 31", "No", "Yes"]], Rows(cut));
    }

    [Fact]
    public void Render_Shelves_HeadsEachColumn()
    {
        _shelves.Rows.Add(Christmas);

        var cut = Render<ShelvesTab>();

        Assert.Equal(["Name", "Season", "Active", "In season", ""], cut.FindAll(".shelves-list th").Select(heading => heading.TextContent));
    }

    // UI-6: an empty list says so, rather than showing an empty table. Staff Picks shelves are never listed here.
    [Fact]
    public void Render_NoShelves_SaysSo()
    {
        var cut = Render<ShelvesTab>();

        Assert.Contains(cut.FindAll("p"), p => p.TextContent == "No seasonal shelves yet.");
        Assert.Empty(cut.FindAll("table"));
    }

    // UI-6: the line says the tab is reading, with no form whose shelf would have no list to join, until the shelves
    // are in.
    [Fact]
    public async Task Render_WhileReading_SaysSoUntilTheyAreIn()
    {
        var reading = new TaskCompletionSource<IReadOnlyList<SeasonalShelfRow>>();
        _shelves.Listing = reading.Task;
        var cut = Render<ShelvesTab>();
        Assert.Equal("Reading the shelves...", Line(cut));
        Assert.Empty(cut.FindAll("form"));

        await cut.InvokeAsync(() => reading.SetResult([Christmas]));

        Assert.Equal(Prompt, Line(cut));
        Assert.Equal([["Christmas", "December 1 to December 25", "Yes", "No"]], Rows(cut));
    }

    // Nothing to put the cursor in until the boxes are drawn.
    [Fact]
    public void Render_WhileReading_FocusesNothingYet()
    {
        _shelves.Listing = new TaskCompletionSource<IReadOnlyList<SeasonalShelfRow>>().Task;

        _ = Render<ShelvesTab>();

        Assert.Empty(JSInterop.Invocations);
    }

    // UI-6: one line always says what to do.
    [Fact]
    public void Render_ShelvesRead_SaysWhatToTypeAndPick()
    {
        var cut = Render<ShelvesTab>();

        Assert.Equal(Prompt, Line(cut));
    }

    // UI-3: the Store Manager can type the first shelf's name as soon as the tab opens.
    [Fact]
    public void Render_ShelvesRead_FocusesTheNameBox()
    {
        var cut = Render<ShelvesTab>();

        AssertFocusedLast(ElementId(cut, NameBox), focuses: 1);
    }

    // The twelve months, each under its name, with its number as its value, which is what Core takes.
    [Theory]
    [InlineData(FromMonthList)]
    [InlineData(ToMonthList)]
    public void Render_MonthList_OffersTheTwelveMonthsInOrder(string list)
    {
        var cut = Render<ShelvesTab>();

        var options = cut.FindAll($"{list} option");

        Assert.Equal(
            ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"],
            options.Select(option => option.TextContent));
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12"], options.Select(option => option.GetAttribute("value")));
    }

    // Core throws for a month that isn't one of the twelve, so each list has one picked from the start: January, the
    // one it shows.
    [Theory]
    [InlineData(FromMonthList)]
    [InlineData(ToMonthList)]
    public void Render_MonthList_StartsOnJanuary(string list)
    {
        var cut = Render<ShelvesTab>();

        Assert.Equal(("1", "January"), (cut.Find(list).GetAttribute("value"), Chosen(cut.Find(list))));
    }

    // SEC-5: each box stops at what fits, Shelf.MaxNameLength for the name and two digits for a day. Core reads what was
    // typed.
    [Fact]
    public void Render_Boxes_CapWhatFits()
    {
        var cut = Render<ShelvesTab>();

        var caps = cut.FindAll("input").Select(box => (box.Id, box.GetAttribute("maxlength")));

        Assert.Equal([("shelves-name", "30"), ("shelves-from-day", "2"), ("shelves-to-day", "2")], caps);
    }

    // ARCH-5: one call to Core, with the boxes as typed, spaces and all, and the months as picked. Core cleans and reads
    // them.
    [Fact]
    public async Task Submit_NewShelf_AddsItOnceAsTypedAndPicked()
    {
        var cut = Render<ShelvesTab>();
        await FillInAsync(cut, " Halloween ", 10, " 1", 11, "2 ");

        await cut.Find("form").SubmitAsync();

        Assert.Equal(["Add ' Halloween ' 10 ' 1' 11 '2 '"], _shelves.Calls);
    }

    // UI-3: a text box submits its form by itself, and keyboard.js has Enter in a list submit the list's own form, so
    // each box and list is in the one form Add submits.
    [Theory]
    [InlineData(NameBox)]
    [InlineData(FromMonthList)]
    [InlineData(FromDayBox)]
    [InlineData(ToMonthList)]
    [InlineData(ToDayBox)]
    public async Task Submit_EnterInEachBoxOrList_AddsTheShelfOnce(string control)
    {
        var cut = Render<ShelvesTab>();
        await FillInAsync(cut, "Halloween", 10, "1", 10, "31");
        var form = cut.Find(control).Closest("form");
        Assert.NotNull(form);

        await form.SubmitAsync();

        Assert.Equal(["Add 'Halloween' 10 '1' 10 '31'"], _shelves.Calls);
    }

    [Fact]
    public async Task ClickAdd_ShelfFilledIn_AddsIt()
    {
        var cut = Render<ShelvesTab>();
        await FillInAsync(cut, "Halloween", 10, "1", 10, "31");

        await Button(cut, "Add").ClickAsync(new MouseEventArgs());

        Assert.Equal(["Add 'Halloween' 10 '1' 10 '31'"], _shelves.Calls);
    }

    // A shelf added here goes at the end, where the Store Manager sees it, rather than among the others, with whether
    // it's in season as Core answered.
    [Fact]
    public async Task Submit_NewShelf_AddsItAtTheEnd()
    {
        _shelves.Rows.AddRange([Christmas, Summer]);
        var cut = Render<ShelvesTab>();
        await FillInAsync(cut, "Halloween", 10, "1", 10, "31");

        await cut.Find("form").SubmitAsync();

        Assert.Equal(
            [
                ["Christmas", "December 1 to December 25", "Yes", "No"],
                ["Summer", "June 21 to September 22", "No", "No"],
                ["Halloween", "October 1 to October 31", "Yes", "Yes"],
            ],
            Rows(cut));
    }

    // UI-6: the line says what Core saved, with the name as Core cleaned it.
    [Fact]
    public async Task Submit_NewShelf_SaysWhatWasAdded()
    {
        _shelves.Adding = Task.FromResult(new SeasonalShelfChange(SeasonalShelfChangeStatus.Saved, Halloween));
        var cut = Render<ShelvesTab>();
        await FillInAsync(cut, " Halloween ", 10, "1", 10, "31");

        await cut.Find("form").SubmitAsync();

        Assert.Equal("Added Halloween, October 1 to October 31.", Line(cut));
    }

    // So the next shelf can be typed straight away, with both lists back on January.
    [Fact]
    public async Task Submit_NewShelf_EmptiesTheForm()
    {
        var cut = Render<ShelvesTab>();
        await FillInAsync(cut, "Halloween", 10, "1", 11, "2");

        await cut.Find("form").SubmitAsync();

        Assert.Equal(("", "1", "", "1", ""), Form(cut));
    }

    // UI-3: the name box has the focus again, for the next shelf. A save that waits disables the form, which takes the
    // focus from it.
    [Fact]
    public async Task Submit_NewShelf_FocusesTheNameBox()
    {
        var cut = Render<ShelvesTab>();
        var nameBox = ElementId(cut, NameBox);
        await FillInAsync(cut, "Halloween", 10, "1", 10, "31");

        await cut.Find("form").SubmitAsync();

        AssertFocusedLast(nameBox, focuses: 2);
    }

    // Core decides what counts as a name, so the tab asks it, and says what Core answered, worded unlike the prompt so
    // it's seen to be a refusal.
    [Fact]
    public async Task Submit_NoName_SaysThereIsNoNameYet()
    {
        _shelves.Adding = Refused(SeasonalShelfChangeStatus.NoName);
        var cut = Render<ShelvesTab>();

        await cut.Find("form").SubmitAsync();

        Assert.Equal("No name yet. Type it, then press Enter.", Line(cut));
    }

    // Which day isn't one, and the days its month has: February's are 1 to 29, since a season has no year.
    [Fact]
    public async Task Submit_NotAFromDay_SaysWhichDayAndTheDaysItsMonthHas()
    {
        _shelves.Adding = Refused(SeasonalShelfChangeStatus.NotAFromDay);
        var cut = Render<ShelvesTab>();
        await FillInAsync(cut, "Leap Day", 2, "30", 3, "1");

        await cut.Find("form").SubmitAsync();

        Assert.Equal("The From day isn't a day of February. Type a day from 1 to 29.", Line(cut));
    }

    [Fact]
    public async Task Submit_NotAToDay_SaysWhichDayAndTheDaysItsMonthHas()
    {
        _shelves.Adding = Refused(SeasonalShelfChangeStatus.NotAToDay);
        var cut = Render<ShelvesTab>();
        await FillInAsync(cut, "Spring Break", 3, "28", 4, "31");

        await cut.Find("form").SubmitAsync();

        Assert.Equal("The To day isn't a day of April. Type a day from 1 to 30.", Line(cut));
    }

    // DATA-11: a name is one shelf's, whatever its letter case, so the line names the shelf that has it, as it's saved.
    [Fact]
    public async Task Submit_NameInUse_SaysWhichShelfHasIt()
    {
        _shelves.Adding = Refused(SeasonalShelfChangeStatus.NameInUse, Halloween);
        var cut = Render<ShelvesTab>();
        await FillInAsync(cut, "HALLOWEEN", 10, "1", 10, "31");

        await cut.Find("form").SubmitAsync();

        Assert.Equal("There's a shelf called Halloween already. Type a name no shelf has yet.", Line(cut));
    }

    // A deactivated shelf keeps its name, so it's reactivated rather than added again.
    [Fact]
    public async Task Submit_NameOfADeactivatedShelf_SaysToReactivateIt()
    {
        _shelves.Adding = Refused(SeasonalShelfChangeStatus.NameInUse, Summer);
        var cut = Render<ShelvesTab>();

        await cut.Find("form").SubmitAsync();

        Assert.Equal(
            "There's a shelf called Summer already, and it's deactivated. Reactivate Summer instead, or type a name no shelf has yet.",
            Line(cut));
    }

    // What was typed and picked stays, so the slip can be seen and fixed.
    [Fact]
    public async Task Submit_Refused_KeepsWhatWasTypedAndPicked()
    {
        _shelves.Adding = Refused(SeasonalShelfChangeStatus.NotAFromDay);
        var cut = Render<ShelvesTab>();
        await FillInAsync(cut, "Leap Day", 2, "30", 3, "1");

        await cut.Find("form").SubmitAsync();

        Assert.Equal(("Leap Day", "2", "30", "3", "1"), Form(cut));
    }

    // UI-3: the box to fix has the cursor.
    [Theory]
    [InlineData(SeasonalShelfChangeStatus.NoName, NameBox)]
    [InlineData(SeasonalShelfChangeStatus.NotAFromDay, FromDayBox)]
    [InlineData(SeasonalShelfChangeStatus.NotAToDay, ToDayBox)]
    [InlineData(SeasonalShelfChangeStatus.NameInUse, NameBox)]
    public async Task Submit_Refused_FocusesTheBoxToFix(SeasonalShelfChangeStatus status, string box)
    {
        _shelves.Adding = Refused(status, status == SeasonalShelfChangeStatus.NameInUse ? Halloween : null);
        var cut = Render<ShelvesTab>();
        var boxId = ElementId(cut, box);

        await cut.Find("form").SubmitAsync();

        AssertFocusedLast(boxId, focuses: 2);
    }

    // UI-6: the line says what's happening while Core saves, and nothing can be typed, picked, added or changed.
    [Fact]
    public async Task Submit_WhileSaving_SaysSoAndDisablesEveryControl()
    {
        var adding = new TaskCompletionSource<SeasonalShelfChange>();
        _shelves.Adding = adding.Task;
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();

        var submit = cut.Find("form").SubmitAsync();

        Assert.Equal("Saving the shelf...", Line(cut));
        Assert.All(cut.FindAll("input, select, button"), control => Assert.True(control.HasAttribute("disabled")));
        adding.SetResult(new SeasonalShelfChange(SeasonalShelfChangeStatus.NoName, null));
        await submit;
    }

    // A second Enter while Core saves would add the shelf twice.
    [Fact]
    public async Task Submit_TwiceWhileSaving_AddsOnce()
    {
        var adding = new TaskCompletionSource<SeasonalShelfChange>();
        _shelves.Adding = adding.Task;
        var cut = Render<ShelvesTab>();
        var form = cut.Find("form");

        var first = form.SubmitAsync();
        var second = form.SubmitAsync();
        adding.SetResult(new SeasonalShelfChange(SeasonalShelfChangeStatus.NoName, null));
        await Task.WhenAll(first, second);

        Assert.Single(_shelves.Calls);
    }

    // UI-6: while the tab is open, a save that fails goes on to the renderer, which hands it to the screen's
    // ErrorBoundary; rendered alone here, it comes back out of the submit. ErrorHub isn't told.
    [Fact]
    public async Task Submit_SaveFails_LeavesItToTheErrorBoundary()
    {
        _shelves.Adding = Task.FromException<SeasonalShelfChange>(new InvalidOperationException("The save failed."));
        var cut = Render<ShelvesTab>();

        var submit = () => cut.Find("form").SubmitAsync();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(submit);
        Assert.Equal("The save failed.", thrown.Message);
        Assert.False(_crashed);
    }

    // OBS-5: Core answers with a status above, so any other is a bug, such as one Core gained later.
    [Fact]
    public async Task Submit_StatusTheTabDoesNotKnow_Throws()
    {
        _shelves.Adding = Refused((SeasonalShelfChangeStatus)99);
        var cut = Render<ShelvesTab>();

        var submit = () => cut.Find("form").SubmitAsync();

        await Assert.ThrowsAsync<UnreachableException>(submit);
    }

    [Fact]
    public async Task Rename_Clicked_PutsTheNameInABox()
    {
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();

        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());

        Assert.Equal(("Christmas", "30"), (Value(cut, ".shelves-rename"), cut.Find(".shelves-rename").GetAttribute("maxlength")));
    }

    // Renaming is for typos. ARCH-5: one call to Core, with the name as typed, and the list shows the shelf as Core
    // saved it, its name cleaned.
    [Fact]
    public async Task Rename_EnterPressed_RenamesOnceAsTyped()
    {
        _shelves.Rows.AddRange([Christmas with { Name = "Christmsa" }, Halloween]);
        _shelves.Renaming = Task.FromResult(new SeasonalShelfChange(SeasonalShelfChangeStatus.Saved, Christmas));
        var cut = Render<ShelvesTab>();
        await RowButton(cut, 0, "Rename").ClickAsync(new MouseEventArgs());
        await cut.Find(".shelves-rename").InputAsync(" Christmas");

        await cut.Find(".shelves-rename").KeyDownAsync(Enter);

        Assert.Equal(["Rename 1 ' Christmas'"], _shelves.Calls);
        Assert.Equal(
            [["Christmas", "December 1 to December 25", "Yes", "No"], ["Halloween", "October 1 to October 31", "Yes", "Yes"]],
            Rows(cut));
    }

    // UI-6: the line says what was saved, with the name as Core cleaned it.
    [Fact]
    public async Task Rename_Saved_SaysWhatWasSaved()
    {
        _shelves.Rows.Add(Christmas with { Name = "Christmsa" });
        _shelves.Renaming = Task.FromResult(new SeasonalShelfChange(SeasonalShelfChangeStatus.Saved, Christmas));
        var cut = Render<ShelvesTab>();
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());
        await cut.Find(".shelves-rename").InputAsync(" Christmas ");

        await Button(cut, "Save").ClickAsync(new MouseEventArgs());

        Assert.Equal("Saved. The shelf is called Christmas.", Line(cut));
    }

    // UI-3: Esc leaves the shelf as it was, and asks Core nothing.
    [Fact]
    public async Task Rename_EscPressed_KeepsTheName()
    {
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());
        await cut.Find(".shelves-rename").InputAsync("Xmas");

        await cut.Find(".shelves-rename").KeyDownAsync(Escape);

        Assert.Empty(_shelves.Calls);
        Assert.Equal([["Christmas", "December 1 to December 25", "Yes", "No"]], Rows(cut));
    }

    // The box stays open, so the name can be typed again.
    [Fact]
    public async Task Rename_BlankName_SaysAShelfNeedsOne()
    {
        _shelves.Rows.Add(Christmas);
        _shelves.Renaming = Refused(SeasonalShelfChangeStatus.NoName);
        var cut = Render<ShelvesTab>();
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());
        await cut.Find(".shelves-rename").InputAsync(" ");

        await cut.Find(".shelves-rename").KeyDownAsync(Enter);

        Assert.Equal("A shelf needs a name.", Line(cut));
        Assert.Single(cut.FindAll(".shelves-rename"));
    }

    // As for a new shelf: the line names the shelf that has it, and says to reactivate a deactivated one.
    [Fact]
    public async Task Rename_NameOfADeactivatedShelf_SaysToReactivateIt()
    {
        _shelves.Rows.AddRange([Christmas, Summer]);
        _shelves.Renaming = Refused(SeasonalShelfChangeStatus.NameInUse, Summer);
        var cut = Render<ShelvesTab>();
        await RowButton(cut, 0, "Rename").ClickAsync(new MouseEventArgs());
        await cut.Find(".shelves-rename").InputAsync("summer");

        await cut.Find(".shelves-rename").KeyDownAsync(Enter);

        Assert.Equal(
            "There's a shelf called Summer already, and it's deactivated. Reactivate Summer instead, or type a name no shelf has yet.",
            Line(cut));
    }

    // UI-3: a refused name keeps the cursor in its box, to fix.
    [Theory]
    [InlineData(SeasonalShelfChangeStatus.NoName)]
    [InlineData(SeasonalShelfChangeStatus.NameInUse)]
    public async Task Rename_Refused_FocusesTheShelfsBoxAgain(SeasonalShelfChangeStatus status)
    {
        _shelves.Rows.Add(Christmas);
        _shelves.Renaming = Refused(status, status == SeasonalShelfChangeStatus.NameInUse ? Halloween : null);
        var cut = Render<ShelvesTab>();
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());
        var boxId = ElementId(cut, ".shelves-rename");

        await cut.Find(".shelves-rename").KeyDownAsync(Enter);

        AssertFocusedLast(boxId, focuses: 3);
    }

    // A day's statuses are a season's, and Core never gives one for a name.
    [Fact]
    public async Task Rename_StatusTheTabDoesNotKnow_Throws()
    {
        _shelves.Rows.Add(Christmas);
        _shelves.Renaming = Refused(SeasonalShelfChangeStatus.NotAFromDay);
        var cut = Render<ShelvesTab>();
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());

        var save = () => cut.Find(".shelves-rename").KeyDownAsync(Enter);

        await Assert.ThrowsAsync<UnreachableException>(save);
    }

    // The lists and boxes start at the season as saved, each in its place, with each day as Core numbers it.
    [Fact]
    public async Task ChangeDates_Clicked_PutsTheSeasonInTheListsAndBoxes()
    {
        _shelves.Rows.Add(Summer);
        var cut = Render<ShelvesTab>();

        await Button(cut, "Change dates").ClickAsync(new MouseEventArgs());

        Assert.Equal(("6", "21", "9", "22"), EditedSeason(cut));
        Assert.Equal(("June", "September"), (Chosen(cut.Find(".shelves-redate-from-month")), Chosen(cut.Find(".shelves-redate-to-month"))));
    }

    // SEC-5, as for a new shelf's days.
    [Fact]
    public async Task ChangeDates_Clicked_CapsTheDaysAtTwoDigits()
    {
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();

        await Button(cut, "Change dates").ClickAsync(new MouseEventArgs());

        Assert.Equal(["2", "2"], cut.FindAll(".shelves-redate input").Select(box => box.GetAttribute("maxlength")));
    }

    // ARCH-5: one call to Core, with the days as typed and the months as picked, and the list shows the season as
    // saved, here one that runs over New Year.
    [Fact]
    public async Task ChangeDates_EnterPressed_SetsTheSeasonOnceAsTypedAndPicked()
    {
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();
        await Button(cut, "Change dates").ClickAsync(new MouseEventArgs());
        await cut.Find(".shelves-redate-to-month").ChangeAsync(new ChangeEventArgs { Value = "1" });
        await cut.Find(".shelves-redate-to-day").InputAsync("6 ");

        await cut.Find(".shelves-redate-to-day").KeyDownAsync(Enter);

        Assert.Equal(["SetSeason 1 12 '1' 1 '6 '"], _shelves.Calls);
        Assert.Equal([["Christmas", "December 1 to January 6", "Yes", "No"]], Rows(cut));
    }

    [Fact]
    public async Task ChangeDates_Saved_SaysWhatWasSaved()
    {
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();
        await Button(cut, "Change dates").ClickAsync(new MouseEventArgs());
        await cut.Find(".shelves-redate-to-month").ChangeAsync(new ChangeEventArgs { Value = "1" });
        await cut.Find(".shelves-redate-to-day").InputAsync("6");

        await Button(cut, "Save").ClickAsync(new MouseEventArgs());

        Assert.Equal("Saved. Christmas runs December 1 to January 6.", Line(cut));
    }

    // Core answers with whether the shelf is in season now, so new dates move it into season, or out of it, at once.
    [Theory]
    [InlineData(1, 10, "20", "Yes")] // Christmas from October 20 takes in October 23
    [InlineData(2, 10, "24", "No")] // Halloween from October 24 leaves it out
    public async Task ChangeDates_Saved_ShowsWhetherItsInSeasonNow(int shelfId, int fromMonth, string fromDay, string inSeason)
    {
        _shelves.Rows.AddRange([Christmas, Halloween]);
        var cut = Render<ShelvesTab>();
        await RowButton(cut, shelfId - 1, "Change dates").ClickAsync(new MouseEventArgs());
        await cut.Find(".shelves-redate-from-month").ChangeAsync(new ChangeEventArgs { Value = $"{fromMonth}" });
        await cut.Find(".shelves-redate-from-day").InputAsync(fromDay);

        await cut.Find(".shelves-redate-from-day").KeyDownAsync(Enter);

        Assert.Equal(inSeason, Rows(cut)[shelfId - 1][3]);
    }

    // UI-3: Esc leaves the season as it was, and asks Core nothing.
    [Fact]
    public async Task ChangeDates_EscPressed_KeepsTheSeason()
    {
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();
        await Button(cut, "Change dates").ClickAsync(new MouseEventArgs());
        await cut.Find(".shelves-redate-to-month").ChangeAsync(new ChangeEventArgs { Value = "1" });
        await cut.Find(".shelves-redate-to-day").InputAsync("6");

        await cut.Find(".shelves-redate-to-day").KeyDownAsync(Escape);

        Assert.Empty(_shelves.Calls);
        Assert.Equal([["Christmas", "December 1 to December 25", "Yes", "No"]], Rows(cut));
    }

    // As for a new shelf, with the month the Store Manager picked for that day.
    [Theory]
    [InlineData(SeasonalShelfChangeStatus.NotAFromDay, "The From day isn't a day of November. Type a day from 1 to 30.")]
    [InlineData(SeasonalShelfChangeStatus.NotAToDay, "The To day isn't a day of February. Type a day from 1 to 29.")]
    public async Task ChangeDates_NotADay_SaysWhichDayAndTheDaysItsMonthHas(SeasonalShelfChangeStatus status, string line)
    {
        _shelves.Rows.Add(Christmas);
        _shelves.Redating = Refused(status);
        var cut = Render<ShelvesTab>();
        await Button(cut, "Change dates").ClickAsync(new MouseEventArgs());
        await cut.Find(".shelves-redate-from-month").ChangeAsync(new ChangeEventArgs { Value = "11" });
        await cut.Find(".shelves-redate-to-month").ChangeAsync(new ChangeEventArgs { Value = "2" });

        await cut.Find(".shelves-redate-to-day").KeyDownAsync(Enter);

        Assert.Equal(line, Line(cut));
        Assert.Equal(("11", "1", "2", "25"), EditedSeason(cut));
    }

    // UI-3: the day to fix has the cursor.
    [Theory]
    [InlineData(SeasonalShelfChangeStatus.NotAFromDay, ".shelves-redate-from-day")]
    [InlineData(SeasonalShelfChangeStatus.NotAToDay, ".shelves-redate-to-day")]
    public async Task ChangeDates_NotADay_FocusesTheDayToFix(SeasonalShelfChangeStatus status, string box)
    {
        _shelves.Rows.Add(Christmas);
        _shelves.Redating = Refused(status);
        var cut = Render<ShelvesTab>();
        await Button(cut, "Change dates").ClickAsync(new MouseEventArgs());
        var boxId = ElementId(cut, box);

        await cut.Find(box).KeyDownAsync(Enter);

        AssertFocusedLast(boxId, focuses: 3);
    }

    // A name's statuses are a name's, and Core never gives one for a season.
    [Fact]
    public async Task ChangeDates_StatusTheTabDoesNotKnow_Throws()
    {
        _shelves.Rows.Add(Christmas);
        _shelves.Redating = Refused(SeasonalShelfChangeStatus.NameInUse, Halloween);
        var cut = Render<ShelvesTab>();
        await Button(cut, "Change dates").ClickAsync(new MouseEventArgs());

        var save = () => cut.Find(".shelves-redate-from-day").KeyDownAsync(Enter);

        await Assert.ThrowsAsync<UnreachableException>(save);
    }

    // UI-3: in each of a shelf's boxes and lists, Enter saves, with one call to Core.
    [Theory]
    [InlineData("Rename", ".shelves-rename", "Rename 1 'Christmas'")]
    [InlineData("Change dates", ".shelves-redate-from-month", "SetSeason 1 12 '1' 12 '25'")]
    [InlineData("Change dates", ".shelves-redate-from-day", "SetSeason 1 12 '1' 12 '25'")]
    [InlineData("Change dates", ".shelves-redate-to-month", "SetSeason 1 12 '1' 12 '25'")]
    [InlineData("Change dates", ".shelves-redate-to-day", "SetSeason 1 12 '1' 12 '25'")]
    public async Task Edit_EnterInEachBoxOrList_SavesOnce(string button, string control, string call)
    {
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();
        await Button(cut, button).ClickAsync(new MouseEventArgs());

        await cut.Find(control).KeyDownAsync(Enter);

        Assert.Equal([call], _shelves.Calls);
    }

    // And Esc in each leaves the shelf as it was.
    [Theory]
    [InlineData("Rename", ".shelves-rename")]
    [InlineData("Change dates", ".shelves-redate-from-month")]
    [InlineData("Change dates", ".shelves-redate-from-day")]
    [InlineData("Change dates", ".shelves-redate-to-month")]
    [InlineData("Change dates", ".shelves-redate-to-day")]
    public async Task Edit_EscInEachBoxOrList_LeavesTheShelfAsItWas(string button, string control)
    {
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();
        await Button(cut, button).ClickAsync(new MouseEventArgs());

        await cut.Find(control).KeyDownAsync(Escape);

        Assert.Empty(_shelves.Calls);
        Assert.Empty(cut.FindAll(".shelves-rename, .shelves-redate"));
        Assert.Equal(["Rename", "Change dates", "Deactivate"], cut.FindAll("td button").Select(b => b.TextContent));
    }

    // UI-6: while a shelf is fixed, the form the prompt points to waits, so the line says how to finish, in place of
    // what the last change came to.
    [Theory]
    [InlineData("Rename", "Fix the name, then press Enter. Esc leaves it as it was.")]
    [InlineData("Change dates", "Pick the months and type the days, then press Enter. Esc leaves them as they were.")]
    public async Task Edit_Clicked_SaysHowToFinish(string button, string line)
    {
        _shelves.Rows.Add(Christmas);
        _shelves.Adding = Refused(SeasonalShelfChangeStatus.NoName);
        var cut = Render<ShelvesTab>();
        await cut.Find("form").SubmitAsync();

        await Button(cut, button).ClickAsync(new MouseEventArgs());

        Assert.Equal(line, Line(cut));
    }

    // UI-6: once the shelf is left as it was, the line says what to do next, not why a box was refused.
    [Fact]
    public async Task Edit_EscPressedAfterARefusal_SaysWhatToDo()
    {
        _shelves.Rows.Add(Christmas);
        _shelves.Redating = Refused(SeasonalShelfChangeStatus.NotAToDay);
        var cut = Render<ShelvesTab>();
        await Button(cut, "Change dates").ClickAsync(new MouseEventArgs());
        await cut.Find(".shelves-redate-to-day").KeyDownAsync(Enter);

        await cut.Find(".shelves-redate-to-day").KeyDownAsync(Escape);

        Assert.Equal(Prompt, Line(cut));
    }

    // UI-3: the shelf's own box, or its first list, has the cursor, to type or pick straight away.
    [Theory]
    [InlineData("Rename", ".shelves-rename")]
    [InlineData("Change dates", ".shelves-redate-from-month")]
    public async Task Edit_Clicked_FocusesTheShelfsFirstBoxOrList(string button, string control)
    {
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();

        await Button(cut, button).ClickAsync(new MouseEventArgs());

        AssertFocusedLast(ElementId(cut, control), focuses: 2);
    }

    // UI-3: once the shelf is saved, or left as it was, the name box has the cursor again, for the next shelf.
    [Theory]
    [InlineData("Rename", ".shelves-rename", "Enter")]
    [InlineData("Rename", ".shelves-rename", "Escape")]
    [InlineData("Change dates", ".shelves-redate-to-day", "Enter")]
    [InlineData("Change dates", ".shelves-redate-to-day", "Escape")]
    public async Task Edit_Finished_FocusesTheNameBox(string button, string control, string key)
    {
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();
        var nameBox = ElementId(cut, NameBox);
        await Button(cut, button).ClickAsync(new MouseEventArgs());

        await cut.Find(control).KeyDownAsync(new KeyboardEventArgs { Key = key });

        AssertFocusedLast(nameBox, focuses: 3);
    }

    // One thing at a time: while a name is fixed, only its box, Save and Cancel can be used.
    [Fact]
    public async Task Rename_Clicked_LocksTheFormAndTheOtherButtons()
    {
        _shelves.Rows.AddRange([Christmas, Halloween]);
        var cut = Render<ShelvesTab>();

        await RowButton(cut, 0, "Rename").ClickAsync(new MouseEventArgs());

        Assert.Equal(["Christmas's name", "Save", "Cancel"], Usable(cut));
    }

    // And while a season is fixed, only its lists, its boxes, Save and Cancel.
    [Fact]
    public async Task ChangeDates_Clicked_LocksTheFormAndTheOtherButtons()
    {
        _shelves.Rows.AddRange([Christmas, Halloween]);
        var cut = Render<ShelvesTab>();

        await RowButton(cut, 0, "Change dates").ClickAsync(new MouseEventArgs());

        Assert.Equal(
            ["Christmas's From month", "Christmas's From day", "Christmas's To month", "Christmas's To day", "Save", "Cancel"],
            Usable(cut));
    }

    // The form waits while a shelf is fixed, so an Enter that reaches it anyway adds nothing.
    [Fact]
    public async Task Submit_WhileAShelfIsFixed_AddsNothing()
    {
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();
        await FillInAsync(cut, "Halloween", 10, "1", 10, "31");
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());

        await cut.Find("form").SubmitAsync();

        Assert.Empty(_shelves.Calls);
    }

    // As for the form: another shelf's buttons, pressed while a shelf is fixed, change nothing, and the shelf being
    // fixed stays the one being fixed.
    [Theory]
    [InlineData("Rename")]
    [InlineData("Change dates")]
    [InlineData("Deactivate")]
    public async Task RowButton_WhileAnotherShelfIsFixed_ChangesNothing(string button)
    {
        _shelves.Rows.AddRange([Christmas, Halloween]);
        var cut = Render<ShelvesTab>();
        await RowButton(cut, 0, "Rename").ClickAsync(new MouseEventArgs());

        await RowButton(cut, 1, button).ClickAsync(new MouseEventArgs());

        Assert.Empty(_shelves.Calls);
        Assert.Equal(["Christmas's name"], cut.FindAll(".shelves-rename, .shelves-redate *[aria-label]").Select(box => box.GetAttribute("aria-label")));
    }

    // While Core saves a name or a season, nothing can be typed, picked or pressed, Save and Cancel included.
    [Theory]
    [InlineData("Rename", ".shelves-rename")]
    [InlineData("Change dates", ".shelves-redate-to-day")]
    public async Task Edit_WhileSaving_DisablesEveryControl(string button, string control)
    {
        var saving = new TaskCompletionSource<SeasonalShelfChange>();
        _shelves.Renaming = saving.Task;
        _shelves.Redating = saving.Task;
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();
        await Button(cut, button).ClickAsync(new MouseEventArgs());

        var save = cut.Find(control).KeyDownAsync(Enter);

        Assert.All(cut.FindAll("input, select, button"), box => Assert.True(box.HasAttribute("disabled")));
        saving.SetResult(new SeasonalShelfChange(SeasonalShelfChangeStatus.Saved, Christmas));
        await save;
    }

    // A second Enter while Core saves would save it again in the middle of the first.
    [Fact]
    public async Task Edit_EnterTwiceWhileSaving_SavesOnce()
    {
        var renaming = new TaskCompletionSource<SeasonalShelfChange>();
        _shelves.Renaming = renaming.Task;
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());
        var box = cut.Find(".shelves-rename");

        var first = box.KeyDownAsync(Enter);
        var second = box.KeyDownAsync(Enter);
        renaming.SetResult(new SeasonalShelfChange(SeasonalShelfChangeStatus.NoName, null));
        await Task.WhenAll(first, second);

        Assert.Single(_shelves.Calls);
    }

    // Esc and Cancel wait too, while Core saves: its answer is on its way, for the shelf being fixed.
    [Theory]
    [InlineData("Escape")]
    [InlineData("Cancel")]
    public async Task Edit_EscOrCancelWhileSaving_KeepsTheShelfOpenForTheAnswer(string how)
    {
        var renaming = new TaskCompletionSource<SeasonalShelfChange>();
        _shelves.Renaming = renaming.Task;
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());
        var save = cut.Find(".shelves-rename").KeyDownAsync(Enter);

        await (how == "Escape"
            ? cut.Find(".shelves-rename").KeyDownAsync(Escape)
            : Button(cut, "Cancel").ClickAsync(new MouseEventArgs()));

        Assert.Equal(("Saving the shelf...", 1), (Line(cut), cut.FindAll(".shelves-rename").Count));
        renaming.SetResult(new SeasonalShelfChange(SeasonalShelfChangeStatus.NoName, null));
        await save;
    }

    // DATA-4: deactivated, the shelf stays in the list, with a button to bring it back. The other shelves stay as they
    // were.
    [Fact]
    public async Task Deactivate_Clicked_ShowsItInactive()
    {
        _shelves.Rows.AddRange([Christmas, Halloween]);
        var cut = Render<ShelvesTab>();

        await RowButton(cut, 0, "Deactivate").ClickAsync(new MouseEventArgs());

        Assert.Equal(["SetActive 1 False"], _shelves.Calls);
        Assert.Equal(
            [["Christmas", "December 1 to December 25", "No", "No"], ["Halloween", "October 1 to October 31", "Yes", "Yes"]],
            Rows(cut));
        Assert.Equal(["Reactivate", "Deactivate"], cut.FindAll("td button").Where(b => b.TextContent.EndsWith("activate", StringComparison.Ordinal)).Select(b => b.TextContent));
    }

    // ADR-0015: a deactivated shelf keeps its movies, for when it's reactivated.
    [Fact]
    public async Task Deactivate_Clicked_SaysWhatWasSaved()
    {
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();

        await Button(cut, "Deactivate").ClickAsync(new MouseEventArgs());

        Assert.Equal("Saved. Christmas is deactivated, and keeps its movies.", Line(cut));
    }

    [Fact]
    public async Task Reactivate_Clicked_ShowsItActiveAndSaysSo()
    {
        _shelves.Rows.Add(Summer);
        var cut = Render<ShelvesTab>();

        await Button(cut, "Reactivate").ClickAsync(new MouseEventArgs());

        Assert.Equal(["SetActive 3 True"], _shelves.Calls);
        Assert.Equal([["Summer", "June 21 to September 22", "Yes", "No"]], Rows(cut));
        Assert.Equal("Saved. Summer is active again.", Line(cut));
    }

    // While Core saves, nothing else can be typed, picked or pressed, the button pressed included.
    [Fact]
    public async Task Deactivate_WhileSaving_DisablesEveryControl()
    {
        var activating = new TaskCompletionSource<SeasonalShelfChange>();
        _shelves.Activating = activating.Task;
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();

        var click = Button(cut, "Deactivate").ClickAsync(new MouseEventArgs());

        Assert.All(cut.FindAll("input, select, button"), control => Assert.True(control.HasAttribute("disabled")));
        activating.SetResult(new SeasonalShelfChange(SeasonalShelfChangeStatus.Saved, Christmas with { IsActive = false }));
        await click;
    }

    // A second press while Core saves would ask again in the middle of the first.
    [Fact]
    public async Task Deactivate_TwiceWhileSaving_SavesOnce()
    {
        var activating = new TaskCompletionSource<SeasonalShelfChange>();
        _shelves.Activating = activating.Task;
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();

        // Found again for the second press: the first one's redraw gives the button a new handler.
        var first = Button(cut, "Deactivate").ClickAsync(new MouseEventArgs());
        var second = Button(cut, "Deactivate").ClickAsync(new MouseEventArgs());
        activating.SetResult(new SeasonalShelfChange(SeasonalShelfChangeStatus.Saved, Christmas with { IsActive = false }));
        await Task.WhenAll(first, second);

        Assert.Single(_shelves.Calls);
    }

    // Core only ever saves a shelf's state, so any other answer is a bug.
    [Fact]
    public async Task Deactivate_StatusTheTabDoesNotKnow_Throws()
    {
        _shelves.Rows.Add(Christmas);
        _shelves.Activating = Refused(SeasonalShelfChangeStatus.NoName);
        var cut = Render<ShelvesTab>();

        var click = () => Button(cut, "Deactivate").ClickAsync(new MouseEventArgs());

        await Assert.ThrowsAsync<UnreachableException>(click);
    }

    // UI-10: a name the family typed shows as typed, markup and all, in the list and in the line.
    [Fact]
    public async Task Submit_NameWithMarkup_ShowsItAsTyped()
    {
        const string Markup = "<b>Boo!</b> & <i>Co</i>";
        var cut = Render<ShelvesTab>();
        await FillInAsync(cut, Markup, 10, "1", 10, "31");

        await cut.Find("form").SubmitAsync();

        Assert.Equal(Markup, Rows(cut)[0][0]);
        Assert.Equal($"Added {Markup}, October 1 to October 31.", Line(cut));
        Assert.Empty(cut.FindAll(".shelves-list b, .shelves-list i, .shelves-prompt b, .shelves-prompt i"));
    }

    // A scan has no place here, and the status bar says what it was, so the tab asks Core nothing, opens no dialog and
    // leaves the line and the cursor where they were.
    [Theory]
    [InlineData(ScanKind.StoreLabel, "RV-00042")]
    [InlineData(ScanKind.MemberCard, "MEM-001")]
    [InlineData(ScanKind.Concession, "CON-POPCORN")]
    [InlineData(ScanKind.Upc, "032429258373")]
    [InlineData(ScanKind.Unknown, "WHAT-IS-THIS")]
    public async Task Scanned_AnyBarcode_DoesNothing(ScanKind kind, string code)
    {
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();

        await ScanAsync(cut, kind, code);

        Assert.Single(_shelves.Tokens); // the read's
        Assert.Null(_dialogs.Current);
        Assert.Equal(Prompt, Line(cut));
        JSInterop.VerifyFocusAsyncInvoke(); // the name box's, as the tab opened, and no other
    }

    // The scanner outlives the tab, so the tab stops listening when the Store Manager leaves it.
    [Fact]
    public async Task Dispose_Any_StopsListeningForScans()
    {
        _ = Render<ShelvesTab>();

        await DisposeComponentsAsync();

        Assert.False(_scanner.IsListenedTo);
    }

    // A scan the scanner's thread raised just before the Store Manager left reaches the tab afterwards, and raises
    // nothing.
    [Fact]
    public async Task Dispose_ScanRaisedJustBefore_RaisesNothing()
    {
        _ = Render<ShelvesTab>();
        var raisedJustBefore = _scanner.RaisedBeforeStopping();
        await DisposeComponentsAsync();

        raisedJustBefore(new Scan(ScanKind.StoreLabel, "RV-00042"));
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind anything the scan started

        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
    }

    // Leaving the tab stops a read still waiting on the database.
    [Fact]
    public async Task Dispose_WhileReading_CancelsTheRead()
    {
        _shelves.Listing = new TaskCompletionSource<IReadOnlyList<SeasonalShelfRow>>().Task;
        _ = Render<ShelvesTab>();

        await DisposeComponentsAsync();

        Assert.True(_shelves.LastToken.IsCancellationRequested);
    }

    // The cancellation leaving causes goes nowhere: it isn't a failure, so ErrorHub isn't told.
    [Fact]
    public async Task Dispose_WhileReading_RaisesNothingOnceCancelled()
    {
        var reading = new TaskCompletionSource<IReadOnlyList<SeasonalShelfRow>>();
        _shelves.Listing = reading.Task;
        _ = Render<ShelvesTab>();
        await DisposeComponentsAsync();

        await Renderer.Dispatcher.InvokeAsync(() => reading.SetCanceled(_shelves.LastToken)); // as EF Core ends a cancelled read

        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
    }

    // And a save. The cancellation leaving causes goes nowhere: WhileBusyAsync catches it.
    [Fact]
    public async Task Dispose_WhileSaving_CancelsTheSaveAndRaisesNothing()
    {
        var adding = new TaskCompletionSource<SeasonalShelfChange>();
        _shelves.Adding = adding.Task;
        var cut = Render<ShelvesTab>();
        var submit = cut.Find("form").SubmitAsync();
        await DisposeComponentsAsync();

        adding.SetCanceled(_shelves.LastToken); // as EF Core ends a save whose token was cancelled
        await submit;

        Assert.True(_shelves.LastToken.IsCancellationRequested);
        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
    }

    // Every call waits on the database with the tab's token, so leaving stops whichever is still waiting. Here each
    // has answered already, and the test checks that leaving cancels the token each was given.
    [Fact]
    public async Task Dispose_AfterEachKindOfChange_CancelsEveryCallsToken()
    {
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();
        await Button(cut, "Rename").ClickAsync(new MouseEventArgs());
        await cut.Find(".shelves-rename").KeyDownAsync(Enter);
        await Button(cut, "Change dates").ClickAsync(new MouseEventArgs());
        await cut.Find(".shelves-redate-to-day").KeyDownAsync(Enter);
        await Button(cut, "Deactivate").ClickAsync(new MouseEventArgs());
        await FillInAsync(cut, "Halloween", 10, "1", 10, "31");
        await cut.Find("form").SubmitAsync();

        await DisposeComponentsAsync();

        Assert.Equal(["Rename 1 'Christmas'", "SetSeason 1 12 '1' 12 '25'", "SetActive 1 False", "Add 'Halloween' 10 '1' 10 '31'"], _shelves.Calls);
        Assert.Equal(5, _shelves.Tokens.Count); // the read's, then each change's
        Assert.All(_shelves.Tokens, token => Assert.True(token.IsCancellationRequested));
    }

    // ADR-0011: an answer that comes after the Store Manager left, rather than being cancelled, changes nothing on
    // screen, since the tab has gone: no cursor moves, and nothing is raised, whatever Core answered.
    [Theory]
    [InlineData(SeasonalShelfChangeStatus.Saved)]
    [InlineData(SeasonalShelfChangeStatus.NoName)]
    [InlineData(SeasonalShelfChangeStatus.NotAFromDay)]
    [InlineData(SeasonalShelfChangeStatus.NotAToDay)]
    [InlineData(SeasonalShelfChangeStatus.NameInUse)]
    public async Task Dispose_AddAnswersAfterTheStoreManagerLeft_ChangesNothing(SeasonalShelfChangeStatus status)
    {
        var adding = new TaskCompletionSource<SeasonalShelfChange>();
        _shelves.Adding = adding.Task;
        var cut = Render<ShelvesTab>();
        var submit = cut.Find("form").SubmitAsync();
        await DisposeComponentsAsync();

        adding.SetResult(new SeasonalShelfChange(status, Answered(status)));
        await submit;

        AssertNothingChangedOnceLeft(focuses: 1);
    }

    // The same for each change to a shelf in the list.
    [Theory]
    [InlineData("Rename", SeasonalShelfChangeStatus.Saved)]
    [InlineData("Rename", SeasonalShelfChangeStatus.NameInUse)]
    [InlineData("Change dates", SeasonalShelfChangeStatus.Saved)]
    [InlineData("Change dates", SeasonalShelfChangeStatus.NotAToDay)]
    public async Task Dispose_EditAnswersAfterTheStoreManagerLeft_ChangesNothing(string button, SeasonalShelfChangeStatus status)
    {
        var saving = new TaskCompletionSource<SeasonalShelfChange>();
        _shelves.Renaming = saving.Task;
        _shelves.Redating = saving.Task;
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();
        await Button(cut, button).ClickAsync(new MouseEventArgs());
        var save = Button(cut, "Save").ClickAsync(new MouseEventArgs());
        await DisposeComponentsAsync();

        saving.SetResult(new SeasonalShelfChange(status, Answered(status)));
        await save;

        AssertNothingChangedOnceLeft(focuses: 2);
    }

    [Fact]
    public async Task Dispose_DeactivationAnswersAfterTheStoreManagerLeft_ChangesNothing()
    {
        var activating = new TaskCompletionSource<SeasonalShelfChange>();
        _shelves.Activating = activating.Task;
        _shelves.Rows.Add(Christmas);
        var cut = Render<ShelvesTab>();
        var click = Button(cut, "Deactivate").ClickAsync(new MouseEventArgs());
        await DisposeComponentsAsync();

        activating.SetResult(new SeasonalShelfChange(SeasonalShelfChangeStatus.Saved, Christmas with { IsActive = false }));
        await click;

        AssertNothingChangedOnceLeft(focuses: 1);
    }

    // #83's route: a save that fails after the Store Manager left has no tab to show it on, so it goes to ErrorHub,
    // with the exception the save threw, for the log to keep its details (OBS-4).
    [Fact]
    public async Task Dispose_WhileSaving_ReportsAFailureToErrorHub()
    {
        var adding = new TaskCompletionSource<SeasonalShelfChange>();
        _shelves.Adding = adding.Task;
        var cut = Render<ShelvesTab>();
        var submit = cut.Find("form").SubmitAsync();
        await DisposeComponentsAsync();
        var failure = new InvalidOperationException("The save failed.");

        adding.SetException(failure);
        await submit;

        Assert.True(_crashed);
        Assert.Equal([(LogLevel.Error, "Unhandled exception from ShelvesTab; reported to the shell", (Exception?)failure)], _errorLog.Entries);
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // The read waits in the busy line too, so one that fails after the Store Manager left takes the same route.
    [Fact]
    public async Task Dispose_WhileReading_ReportsAFailureToErrorHub()
    {
        var reading = new TaskCompletionSource<IReadOnlyList<SeasonalShelfRow>>();
        _shelves.Listing = reading.Task;
        _ = Render<ShelvesTab>();
        await DisposeComponentsAsync();
        var failure = new InvalidOperationException("The read failed.");

        await Renderer.Dispatcher.InvokeAsync(() => reading.SetException(failure));

        Assert.True(_crashed);
        Assert.Equal([(LogLevel.Error, "Unhandled exception from ShelvesTab; reported to the shell", (Exception?)failure)], _errorLog.Entries);
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // While the tab is open, a read that fails goes to its ErrorBoundary, not to ErrorHub.
    [Fact]
    public async Task Render_ReadFails_HandsItToTheErrorBoundary()
    {
        var reading = new TaskCompletionSource<IReadOnlyList<SeasonalShelfRow>>();
        _shelves.Listing = reading.Task;
        _ = Render<ShelvesTab>();

        await Renderer.Dispatcher.InvokeAsync(() => reading.SetException(new InvalidOperationException("The read failed.")));

        Assert.True(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
    }

    private static Task<SeasonalShelfChange> Refused(SeasonalShelfChangeStatus status, SeasonalShelfRow? owner = null) =>
        Task.FromResult(new SeasonalShelfChange(status, owner));

    // The shelf a change's answer carries, as Core gives it: the one saved, here Christmas, or the one with the name,
    // here Halloween. No shelf for a refusal of a name or a day.
    private static SeasonalShelfRow? Answered(SeasonalShelfChangeStatus status) => status switch
    {
        SeasonalShelfChangeStatus.Saved => Christmas,
        SeasonalShelfChangeStatus.NameInUse => Halloween,
        _ => null,
    };

    // Types in each box and picks from each list, as the Store Manager does. A list takes its option's value, which is
    // the month's number.
    private static async Task FillInAsync(
        IRenderedComponent<ShelvesTab> cut, string name, int fromMonth, string fromDay, int toMonth, string toDay)
    {
        await cut.Find(NameBox).InputAsync(name);
        await cut.Find(FromMonthList).ChangeAsync(new ChangeEventArgs { Value = $"{fromMonth}" });
        await cut.Find(FromDayBox).InputAsync(fromDay);
        await cut.Find(ToMonthList).ChangeAsync(new ChangeEventArgs { Value = $"{toMonth}" });
        await cut.Find(ToDayBox).InputAsync(toDay);
    }

    // A scan, as the scanner's thread raises it, then a turn of the tab's own thread, so the tab has handled it before
    // the test looks.
    private async Task ScanAsync(IRenderedComponent<ShelvesTab> cut, ScanKind kind, string code)
    {
        _scanner.Emit(new Scan(kind, code));
        await cut.InvokeAsync(() => { });
    }

    // Once the Store Manager has left, an answer moves no cursor, shows no dialog and raises nothing.
    private void AssertNothingChangedOnceLeft(int focuses)
    {
        JSInterop.VerifyFocusAsyncInvoke(focuses); // only those made while the tab was open
        Assert.Null(_dialogs.Current);
        Assert.False(Renderer.UnhandledException.IsCompleted);
        Assert.False(_crashed);
        Assert.Single(_shelves.Calls);
    }

    private static string Line(IRenderedComponent<ShelvesTab> cut) => cut.Find(".shelves-prompt").TextContent;

    private static string Value(IRenderedComponent<ShelvesTab> cut, string selector) => cut.Find(selector).GetAttribute("value") ?? "";

    // The new shelf's name, months and days, as the form's boxes and lists hold them.
    private static (string Name, string FromMonth, string FromDay, string ToMonth, string ToDay) Form(IRenderedComponent<ShelvesTab> cut) =>
        (Value(cut, NameBox), Value(cut, FromMonthList), Value(cut, FromDayBox), Value(cut, ToMonthList), Value(cut, ToDayBox));

    // The season being fixed, as its lists and boxes hold it.
    private static (string FromMonth, string FromDay, string ToMonth, string ToDay) EditedSeason(IRenderedComponent<ShelvesTab> cut) =>
        (Value(cut, ".shelves-redate-from-month"), Value(cut, ".shelves-redate-from-day"),
            Value(cut, ".shelves-redate-to-month"), Value(cut, ".shelves-redate-to-day"));

    // The choice a list shows: the one whose value Blazor gave the list.
    private static string Chosen(IElement list) =>
        list.QuerySelectorAll("option").Single(option => option.GetAttribute("value") == list.GetAttribute("value")).TextContent;

    private static IElement Button(IRenderedComponent<ShelvesTab> cut, string text) =>
        cut.FindAll("button").Single(button => button.TextContent == text);

    // A button on one shelf's row, counting from the first shelf listed.
    private static IElement RowButton(IRenderedComponent<ShelvesTab> cut, int row, string text) =>
        cut.FindAll(".shelves-list tbody tr")[row].QuerySelectorAll("button").Single(button => button.TextContent == text);

    // Each control that can still be used: a button by its words, a box or a list by the name it's read out by, or its
    // id when it has a label of its own.
    private static IEnumerable<string?> Usable(IRenderedComponent<ShelvesTab> cut) =>
        cut.FindAll("input, select, button")
            .Where(control => !control.HasAttribute("disabled"))
            .Select(control => control.LocalName == "button" ? control.TextContent : control.GetAttribute("aria-label") ?? control.Id);

    // Each shelf's name, season, whether it's active and whether it's in season, as the list shows them.
    private static List<string[]> Rows(IRenderedComponent<ShelvesTab> cut) =>
        [.. cut.FindAll(".shelves-list tbody tr").Select(row => row.QuerySelectorAll("td").Take(4).Select(cell => cell.TextContent).ToArray())];

    // The ID of an element's @ref. bUnit writes it into the markup only on the element's first render, so a test reads
    // it before anything redraws the element.
    private static string? ElementId(IRenderedComponent<ShelvesTab> cut, string selector) =>
        cut.Find(selector).GetAttribute("blazor:elementreference");

    // UI-3: the element focused last, from all the focus calls so far.
    private void AssertFocusedLast(string? elementId, int focuses)
    {
        var focused = Assert.IsType<ElementReference>(JSInterop.VerifyFocusAsyncInvoke(focuses)[^1].Arguments[0]);
        Assert.Equal(elementId, focused.Id);
    }
}
