using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Rewind.Core.Concessions;
using Rewind.Core.Devices;
using Rewind.Core.Labels;
using Rewind.Core.Members;
using Rewind.Core.Sounds;
using Rewind.Core.StoreManager;
using Rewind.UI.Dialogs;
using Rewind.UI.Errors;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class StoreManagerTests : BunitContext
{
    private readonly FakeApplicationLifetime _lifetime = new();
    private readonly FakeSoundPlayer _sounds = new();
    private readonly FakeLabelSheets _sheets = new();
    private readonly DialogService _dialogs;
    private readonly BunitJSModuleInterop _keyboard;

    public StoreManagerTests()
    {
        _dialogs = new DialogService(_sounds);

        Services.AddSingleton<IHostApplicationLifetime>(_lifetime);
        Services.AddSingleton(_dialogs);
        Services.AddSingleton<ISoundPlayer>(_sounds); // Debug builds add Diagnostics, which plays sounds
        Services.AddSingleton<ILabelSheets>(_sheets);
        Services.AddSingleton<IMemberRoster>(new FakeMemberRoster()); // for the Members tab
        Services.AddSingleton<IStorePrices>(new FakeStorePrices()); // for the Prices tab
        Services.AddSingleton<IConcessionBar>(new FakeConcessionBar()); // for the Snacks tab
        Services.AddSingleton<IScanner>(new FakeScanner());
        Services.AddSingleton(new ErrorHub(NullLogger<ErrorHub>.Instance));

        // A sheet opens the print window through keyboard.js (UI-9). Each test that prints sets up how
        // the window answers, so a print nobody expected fails the test.
        _keyboard = JSInterop.SetupModule("./_content/Rewind.UI/js/keyboard.js");
    }

    // F5 opens on Printing, as it opened on its labels and cards before it had tabs. The menu prints from
    // there too.
    [Fact]
    public void Render_ScreenOpens_ShowsPrinting()
    {
        var cut = Render<StoreManager>();

        var open = cut.FindAll(".store-manager-tabs li").Where(tab => tab.GetAttribute("aria-selected") == "true");
        Assert.Equal(["Printing"], open.Select(tab => tab.TextContent));
        Assert.Equal(["Labels and cards", "Concession menu"], cut.FindAll(".store-manager-panel legend").Select(legend => legend.TextContent));
    }

    [Fact]
    public async Task MembersTab_Clicked_ShowsTheMembers()
    {
        var cut = Render<StoreManager>();

        await Tab(cut, "Members").ClickAsync(new MouseEventArgs());

        Assert.Single(cut.FindAll("#members-name"));
        Assert.Empty(cut.FindAll(".labels-and-cards"));
    }

    // UI-3: Enter opens a tab, as it does on the shell's.
    [Fact]
    public async Task MembersTab_EnterPressed_ShowsTheMembers()
    {
        var cut = Render<StoreManager>();

        await Tab(cut, "Members").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Single(cut.FindAll("#members-name"));
    }

    // SEC-6, as for the sheet below: ShellTests checks F5 as it opens, on Printing.
    [Fact]
    public async Task MembersTab_Opened_HasNoHrefLinks()
    {
        var cut = Render<StoreManager>();

        await Tab(cut, "Members").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("a[href]"));
    }

    [Fact]
    public async Task PricesTab_Clicked_ShowsThePrices()
    {
        var cut = Render<StoreManager>();

        await Tab(cut, "Prices").ClickAsync(new MouseEventArgs());

        Assert.Equal("2.00", cut.Find("#prices-rental").GetAttribute("value"));
        Assert.Equal(["false", "false", "true", "false"], cut.FindAll(".store-manager-tabs li").Select(tab => tab.GetAttribute("aria-selected")));
        Assert.Empty(cut.FindAll(".labels-and-cards"));
    }

    // UI-3: Enter opens it, as for Members.
    [Fact]
    public async Task PricesTab_EnterPressed_ShowsThePrices()
    {
        var cut = Render<StoreManager>();

        await Tab(cut, "Prices").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Single(cut.FindAll("#prices-rental"));
    }

    // SEC-6, as for Members.
    [Fact]
    public async Task PricesTab_Opened_HasNoHrefLinks()
    {
        var cut = Render<StoreManager>();

        await Tab(cut, "Prices").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("a[href]"));
    }

    [Fact]
    public async Task SnacksTab_Clicked_ShowsTheSnacks()
    {
        var cut = Render<StoreManager>();

        await Tab(cut, "Snacks").ClickAsync(new MouseEventArgs());

        Assert.Single(cut.FindAll("#snacks-name"));
        Assert.Equal(["false", "false", "false", "true"], cut.FindAll(".store-manager-tabs li").Select(tab => tab.GetAttribute("aria-selected")));
        Assert.Empty(cut.FindAll(".labels-and-cards"));
    }

    // UI-3: Enter opens it, as for Members.
    [Fact]
    public async Task SnacksTab_EnterPressed_ShowsTheSnacks()
    {
        var cut = Render<StoreManager>();

        await Tab(cut, "Snacks").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Single(cut.FindAll("#snacks-name"));
    }

    // SEC-6, as for Members.
    [Fact]
    public async Task SnacksTab_Opened_HasNoHrefLinks()
    {
        var cut = Render<StoreManager>();

        await Tab(cut, "Snacks").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll("a[href]"));
    }

    [Fact]
    public async Task ShutDown_ClerkPressesOk_StopsRewind()
    {
        var cut = Render<StoreManager>();
        var shutDown = PressShutDownAsync(cut);

        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        await shutDown;

        Assert.True(_lifetime.StopRequested);
    }

    [Fact]
    public async Task ShutDown_ClerkCancels_KeepsRewindRunning()
    {
        var cut = Render<StoreManager>();
        var shutDown = PressShutDownAsync(cut);

        await cut.InvokeAsync(() => _dialogs.Close(ok: false));
        await shutDown;

        Assert.False(_lifetime.StopRequested);
    }

    // The error chord means something's wrong, and asking before Shut Down is routine.
    [Fact]
    public async Task ShutDown_ClerkIsAsked_PlaysNoErrorChord()
    {
        var cut = Render<StoreManager>();
        var shutDown = PressShutDownAsync(cut);

        await cut.InvokeAsync(() => _dialogs.Close(ok: false));
        await shutDown;

        Assert.DoesNotContain(Sound.Error, _sounds.Played);
    }

    [Fact]
    public void Render_NextNumbers_ShowsThem()
    {
        var cut = Render<StoreManager>();

        var lines = cut.FindAll(".labels-and-cards-next").Select(line => line.TextContent);
        Assert.Equal(["Next store label: RV-00031", "Next member card: MEM-001"], lines);
    }

    // UI-6: until the numbers are read, there are no labels or cards to print yet. The menu needs none.
    [Fact]
    public void Render_NumbersNotReadYet_ShowsNoLabelOrCardButtons()
    {
        _sheets.Next = new TaskCompletionSource<NextCodes>().Task;

        var cut = Render<StoreManager>();

        Assert.Contains("Reading the next numbers", cut.Find(".labels-and-cards").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.Find(".labels-and-cards").QuerySelectorAll("button"));
    }

    [Fact]
    public async Task PrintLabels_Clicked_ShowsThirtyLabelsInOrder()
    {
        PrintWindowCloses();
        var cut = Render<StoreManager>();

        await Button(cut, "Print 30 labels").ClickAsync(new MouseEventArgs());

        var labels = cut.FindAll(".sheet-label");
        Assert.Equal(30, labels.Count);
        Assert.Equal(Codes(FakeLabelSheets.StoreLabels), labels.Select(label => label.QuerySelector(".sheet-code")?.TextContent));
        Assert.All(labels, label => Assert.NotNull(label.QuerySelector("svg")));
    }

    // The store's name of record comes with the sheet, from StoreSettings.
    [Fact]
    public async Task PrintLabels_Clicked_NamesTheStoreOnEachLabel()
    {
        PrintWindowCloses();
        var cut = Render<StoreManager>();

        await Button(cut, "Print 30 labels").ClickAsync(new MouseEventArgs());

        Assert.All(cut.FindAll(".sheet-label"), label => Assert.Equal("Rewind Video", label.QuerySelector(".sheet-store-name")?.TextContent));
    }

    // Members arrive in Build Plan 3.1, so a card is a numbered blank: the clerk writes the name on.
    [Fact]
    public async Task PrintCards_Clicked_SaysMemberCardUnderTheStoreName()
    {
        PrintWindowCloses();
        var cut = Render<StoreManager>();

        await Button(cut, "Print 10 member cards").ClickAsync(new MouseEventArgs());

        Assert.All(cut.FindAll(".sheet-card"), card => Assert.Equal(
            ("Rewind Video", "Member card"),
            (card.QuerySelector(".sheet-store-name")?.TextContent, card.QuerySelector(".sheet-card-title")?.TextContent)));
    }

    // The clerk's marker needs a place to go: a line above the barcode, so the ink stays off its bars.
    [Fact]
    public async Task PrintCards_Clicked_LeavesALineForTheName()
    {
        PrintWindowCloses();
        var cut = Render<StoreManager>();

        await Button(cut, "Print 10 member cards").ClickAsync(new MouseEventArgs());

        Assert.All(cut.FindAll(".sheet-card"), card => Assert.Equal(
            ["sheet-store-name", "sheet-card-title", "sheet-card-name", "svg", "sheet-code"],
            TopToBottom(card)));
        Assert.All(cut.FindAll(".sheet-card-name"), line => Assert.Equal("Name", line.TextContent));
    }

    // A label is an inch tall, and a name line would push its barcode off it.
    [Fact]
    public async Task PrintLabels_Clicked_LeavesNoLineForAName()
    {
        PrintWindowCloses();
        var cut = Render<StoreManager>();

        await Button(cut, "Print 30 labels").ClickAsync(new MouseEventArgs());

        Assert.All(cut.FindAll(".sheet-label"), label => Assert.Equal(["sheet-store-name", "svg", "sheet-code"], TopToBottom(label)));
    }

    // The print window prints what's on the page, so it opens once the sheet has rendered. Here it stays
    // open until the test closes it, and the labels are counted while it's open.
    //
    // Printing once per sheet is the screen's guard, not this test's: without it, each print's render
    // prints again, and the test host dies of a stack overflow instead of failing an assert. A run of
    // this file that dies without naming a failing test means that guard has gone.
    [Fact]
    public async Task PrintLabels_Clicked_OpensThePrintDialog()
    {
        var print = _keyboard.SetupVoid("print");
        var cut = Render<StoreManager>();

        var click = Button(cut, "Print 30 labels").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(print.Invocations));
        var labelsWhilePrinting = cut.FindAll(".sheet-label").Count;
        print.SetVoidResult();
        await click;

        cut.WaitForAssertion(() => Assert.EndsWith("Press Print this sheet again.", SheetLine(cut), StringComparison.Ordinal));
        Assert.Equal(30, labelsWhilePrinting);
        Assert.Single(print.Invocations); // once for the sheet, not once per render
    }

    // ADR-0006: the print window doesn't say whether the sheet printed, failed or was cancelled, so the
    // line never says it printed.
    [Fact]
    public async Task PrintLabels_PrintWindowClosed_SaysWhichLabels()
    {
        PrintWindowCloses();
        var cut = Render<StoreManager>();

        await Button(cut, "Print 30 labels").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(
            "RV-00031 to RV-00060. Didn't print, or jammed? Press Print this sheet again.",
            SheetLine(cut)));
    }

    // A second click while the sheet is being made would make a second sheet, and spend its numbers.
    [Fact]
    public async Task PrintLabels_WhileReserving_DisablesTheButtons()
    {
        var making = new TaskCompletionSource<SheetReservation>();
        _sheets.Labels = making.Task;
        PrintWindowCloses();
        var cut = Render<StoreManager>();

        var click = Button(cut, "Print 30 labels").ClickAsync(new MouseEventArgs());
        var disabled = new[] { Button(cut, "Print 30 labels"), Button(cut, "Print 10 member cards") }
            .Select(button => button.HasAttribute("disabled"))
            .ToList();
        await cut.InvokeAsync(() => making.SetResult(new SheetReservation(SheetStatus.Reserved, FakeLabelSheets.StoreLabels)));
        await click;

        Assert.Equal([true, true], disabled);
    }

    // While the print window is open, the sheet stays on screen: Done would drop it mid-print, and a
    // second print window would print it twice.
    [Fact]
    public async Task PrintLabels_WhilePrinting_DisablesTheSheetButtons()
    {
        var print = _keyboard.SetupVoid("print");
        var cut = Render<StoreManager>();

        var click = Button(cut, "Print 30 labels").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(print.Invocations));
        var disabled = new[] { Button(cut, "Print this sheet again"), Button(cut, "Done") }
            .Select(button => button.HasAttribute("disabled"))
            .ToList();
        print.SetVoidResult();
        await click;

        Assert.Equal([true, true], disabled);
    }

    // Keys have no disabled attribute, so Esc checks for itself.
    [Fact]
    public async Task KeyDownEscape_WhilePrinting_KeepsTheSheet()
    {
        var print = _keyboard.SetupVoid("print");
        var cut = Render<StoreManager>();
        var click = Button(cut, "Print 30 labels").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(print.Invocations));

        await cut.Find(".sheet-view").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        var labelsAfterEscape = cut.FindAll(".sheet-label").Count;
        print.SetVoidResult();
        await click;
        Assert.Equal(30, labelsAfterEscape);
    }

    // ADR-0006: a sheet that jammed or didn't print prints again with the same numbers, and no new ones
    // are spent.
    [Fact]
    public async Task PrintAgain_Clicked_PrintsTheSameSheet()
    {
        var print = PrintWindowCloses();
        var cut = Render<StoreManager>();
        await PrintSheetAsync(cut, "Print 30 labels");

        await Button(cut, "Print this sheet again").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(2, print.Invocations.Count));
        Assert.Equal(1, _sheets.LabelSheetsMade);
        Assert.Equal(Codes(FakeLabelSheets.StoreLabels), cut.FindAll(".sheet-label .sheet-code").Select(code => code.TextContent));
    }

    [Fact]
    public async Task PrintCards_Clicked_ShowsTenCardsInOrder()
    {
        PrintWindowCloses();
        var cut = Render<StoreManager>();

        await Button(cut, "Print 10 member cards").ClickAsync(new MouseEventArgs());

        var cards = cut.FindAll(".sheet-card");
        Assert.Equal(10, cards.Count);
        Assert.Equal(Codes(FakeLabelSheets.MemberCards), cards.Select(card => card.QuerySelector(".sheet-code")?.TextContent));
        Assert.All(cards, card => Assert.NotNull(card.QuerySelector("svg")));
    }

    [Fact]
    public async Task PrintLabels_OutOfNumbers_ShowsTheErrorDialog()
    {
        _sheets.Labels = Task.FromResult(FakeLabelSheets.OutOfNumbers);
        var cut = Render<StoreManager>();

        var click = Button(cut, "Print 30 labels").ClickAsync(new MouseEventArgs());
        var shown = _dialogs.Current;
        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        await click;

        Assert.Equal(
            new Dialog(Store.Name, "Rewind has used all 99,999 store label numbers.\n\nTell the Store Manager.", DialogIcon.Error),
            shown);
    }

    [Fact]
    public async Task PrintCards_OutOfNumbers_ShowsTheErrorDialog()
    {
        _sheets.Cards = Task.FromResult(FakeLabelSheets.OutOfNumbers);
        var cut = Render<StoreManager>();

        var click = Button(cut, "Print 10 member cards").ClickAsync(new MouseEventArgs());
        var shown = _dialogs.Current;
        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        await click;

        Assert.Equal(
            new Dialog(Store.Name, "Rewind has used all 999 member card numbers.\n\nTell the Store Manager.", DialogIcon.Error),
            shown);
    }

    [Fact]
    public async Task KeyDownEscape_SheetShown_ReturnsToTheNextNumbers()
    {
        PrintWindowCloses();
        var cut = Render<StoreManager>();
        await PrintSheetAsync(cut, "Print 30 labels");
        _sheets.Next = Task.FromResult(new NextCodes("RV-00061", "MEM-001"));

        await cut.Find(".sheet-view").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".print-sheet"));
        Assert.Equal("Next store label: RV-00061", cut.Find(".labels-and-cards-next").TextContent);
    }

    [Fact]
    public async Task Done_Clicked_ReturnsToTheNextNumbers()
    {
        PrintWindowCloses();
        var cut = Render<StoreManager>();
        await PrintSheetAsync(cut, "Print 30 labels");
        _sheets.Next = Task.FromResult(new NextCodes("RV-00061", "MEM-001"));

        await Button(cut, "Done").ClickAsync(new MouseEventArgs());

        Assert.Empty(cut.FindAll(".print-sheet"));
        Assert.Equal("Next store label: RV-00061", cut.Find(".labels-and-cards-next").TextContent);
    }

    // SEC-6: BlazorWebView opens the default browser for a middle-click on an href link, and MainForm
    // can't stop it, so the sheet may have none. ShellTests opens F5, but no sheet.
    [Fact]
    public async Task PrintLabels_SheetShown_HasNoHrefLinks()
    {
        PrintWindowCloses();
        var cut = Render<StoreManager>();

        await PrintSheetAsync(cut, "Print 30 labels");

        Assert.Empty(cut.FindAll("a[href]"));
    }

    // Leaving the screen stops a read still waiting on the database. The three tests after this one
    // cancel Core's answer by hand, so they'd still pass if it didn't.
    [Fact]
    public async Task Dispose_WhileReading_CancelsTheRead()
    {
        _sheets.Next = new TaskCompletionSource<NextCodes>().Task;
        _ = Render<StoreManager>();

        await DisposeComponentsAsync();

        Assert.True(_sheets.LastToken.IsCancellationRequested);
    }

    // The read's cancellation raises nothing, though the screen has no catch for it: ComponentBase
    // ignores a task that was cancelled (Microsoft Learn, "ASP.NET Core Blazor synchronization
    // context"). This catches a Blazor that stops doing so, such as on a move to the next LTS (NET-13).
    [Fact]
    public async Task Dispose_WhileReading_RaisesNothingOnceCancelled()
    {
        var read = new TaskCompletionSource<NextCodes>();
        _sheets.Next = read.Task;
        _ = Render<StoreManager>();
        await DisposeComponentsAsync();

        read.SetCanceled(_sheets.LastToken); // as EF Core ends a query whose token was cancelled
        await Renderer.Dispatcher.InvokeAsync(() => { }); // queued behind the screen's own continuation

        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // Done reads the codes again from an event handler, whose cancelled task ComponentBase ignores too.
    [Fact]
    public async Task Dispose_WhileReadingAfterDone_RaisesNothingOnceCancelled()
    {
        PrintWindowCloses();
        var cut = Render<StoreManager>();
        await PrintSheetAsync(cut, "Print 30 labels");
        var read = new TaskCompletionSource<NextCodes>();
        _sheets.Next = read.Task;
        var done = Button(cut, "Done").ClickAsync(new MouseEventArgs());
        await DisposeComponentsAsync();

        read.SetCanceled(_sheets.LastToken);
        await done;

        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // So does making a sheet, from the print buttons' event handler.
    [Fact]
    public async Task Dispose_WhileMakingASheet_RaisesNothingOnceCancelled()
    {
        var making = new TaskCompletionSource<SheetReservation>();
        _sheets.Labels = making.Task;
        var cut = Render<StoreManager>();
        var click = Button(cut, "Print 30 labels").ClickAsync(new MouseEventArgs());
        await DisposeComponentsAsync();

        making.SetCanceled(_sheets.LastToken);
        await click;

        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // Build Plan 3.1: the concession menu prints from F5, as the labels and cards do, at its true size.
    [Fact]
    public async Task PrintMenu_Clicked_ShowsTheMenu()
    {
        PrintWindowCloses();
        _sheets.Menu = Drawn(FakeLabelSheets.SnackMenu);
        var cut = Render<StoreManager>();

        await Button(cut, "Print the menu").ClickAsync(new MouseEventArgs());

        Assert.Equal(["Candy", "Popcorn", "Soda"], cut.FindAll(".menu-sheet .menu-name").Select(name => name.TextContent));
        Assert.Empty(cut.FindAll(".store-manager-tabs"));
    }

    // As for a sheet, the print window opens once the menu has rendered, and once only.
    [Fact]
    public async Task PrintMenu_Clicked_OpensThePrintDialog()
    {
        var print = _keyboard.SetupVoid("print");
        _sheets.Menu = Drawn(FakeLabelSheets.SnackMenu);
        var cut = Render<StoreManager>();

        var click = Button(cut, "Print the menu").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(print.Invocations));
        var linesWhilePrinting = cut.FindAll(".menu-line").Count;
        print.SetVoidResult();
        await click;

        cut.WaitForAssertion(() => Assert.EndsWith("Print the menu again.", SheetLine(cut), StringComparison.Ordinal));
        Assert.Equal(3, linesWhilePrinting);
        Assert.Single(print.Invocations);
    }

    // The print window doesn't say whether the menu printed. The menu spends nothing, so the line sends the
    // Store Manager back to its own button.
    [Fact]
    public async Task PrintMenu_PrintWindowClosed_SaysHowToPrintItAgain()
    {
        PrintWindowCloses();
        _sheets.Menu = Drawn(FakeLabelSheets.SnackMenu);
        var cut = Render<StoreManager>();

        await PrintTheMenuAsync(cut);

        Assert.Equal("The concession menu. Didn't print, or jammed? Press Done, then Print the menu again.", SheetLine(cut));
    }

    // No Print this sheet again: that's for a sheet whose numbers are spent (ADR-0006).
    [Fact]
    public async Task PrintMenu_MenuShown_HasOnlyDone()
    {
        PrintWindowCloses();
        _sheets.Menu = Drawn(FakeLabelSheets.SnackMenu);
        var cut = Render<StoreManager>();

        await PrintTheMenuAsync(cut);

        Assert.Equal(["Done"], cut.FindAll(".sheet-view button").Select(button => button.TextContent));
    }

    // NET-12: with no snack on sale there's no menu, and the Store Manager is told where snacks come from.
    [Fact]
    public async Task PrintMenu_NothingOnSale_ShowsTheInfoDialog()
    {
        var cut = Render<StoreManager>();

        var click = Button(cut, "Print the menu").ClickAsync(new MouseEventArgs());
        var shown = _dialogs.Current;
        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        await click;

        Assert.Equal(
            new Dialog(Store.Name, "No snack is on sale, so there's nothing to put on the menu.\n\nAdd the snacks on the Snacks tab first.", DialogIcon.Info),
            shown);
    }

    // The menu needs none of the next numbers, so its button is there before they're read.
    [Fact]
    public async Task PrintMenu_BeforeTheNumbersAreRead_PrintsTheMenu()
    {
        PrintWindowCloses();
        _sheets.Next = new TaskCompletionSource<NextCodes>().Task;
        _sheets.Menu = Drawn(FakeLabelSheets.SnackMenu);
        var cut = Render<StoreManager>();

        await Button(cut, "Print the menu").ClickAsync(new MouseEventArgs());

        Assert.Single(cut.FindAll(".menu-sheet"));
    }

    // While the menu is drawn, nothing else starts, as for a sheet.
    [Fact]
    public async Task PrintMenu_WhileDrawing_DisablesThePrintButtons()
    {
        var drawing = new TaskCompletionSource<MenuDrawing>();
        _sheets.Menu = drawing.Task;
        PrintWindowCloses();
        var cut = Render<StoreManager>();

        var click = Button(cut, "Print the menu").ClickAsync(new MouseEventArgs());
        var disabled = new[] { Button(cut, "Print 30 labels"), Button(cut, "Print 10 member cards"), Button(cut, "Print the menu") }
            .Select(button => button.HasAttribute("disabled"))
            .ToList();
        await cut.InvokeAsync(() => drawing.SetResult(new MenuDrawing(MenuStatus.Drawn, FakeLabelSheets.SnackMenu)));
        await click;

        Assert.Equal([true, true, true], disabled);
    }

    // A second click while the menu is drawn would draw it, and open the print window, twice.
    [Fact]
    public async Task PrintMenu_TwiceWhileDrawing_DrawsOnce()
    {
        var drawing = new TaskCompletionSource<MenuDrawing>();
        _sheets.Menu = drawing.Task;
        PrintWindowCloses();
        var cut = Render<StoreManager>();
        var button = Button(cut, "Print the menu");

        var first = button.ClickAsync(new MouseEventArgs());
        var second = button.ClickAsync(new MouseEventArgs());
        await cut.InvokeAsync(() => drawing.SetResult(new MenuDrawing(MenuStatus.Drawn, FakeLabelSheets.SnackMenu)));
        await Task.WhenAll(first, second);

        Assert.Equal(1, _sheets.MenusDrawn);
    }

    // UI-3: Done goes back to the Printing tab, with the focus on Print the menu, to print it again.
    [Fact]
    public async Task Done_AfterTheMenu_FocusesPrintTheMenu()
    {
        PrintWindowCloses();
        _sheets.Menu = Drawn(FakeLabelSheets.SnackMenu);
        var cut = Render<StoreManager>();
        await PrintTheMenuAsync(cut);

        await Button(cut, "Done").ClickAsync(new MouseEventArgs());

        // The button is drawn anew, so its ID is read in the render that drew it, before anything else.
        AssertFocusedLast(Button(cut, "Print the menu").GetAttribute("blazor:elementreference"), focuses: 2);
    }

    // UI-3: Esc is Done here too.
    [Fact]
    public async Task KeyDownEscape_MenuShown_ReturnsToThePrintingTab()
    {
        PrintWindowCloses();
        _sheets.Menu = Drawn(FakeLabelSheets.SnackMenu);
        var cut = Render<StoreManager>();
        await PrintTheMenuAsync(cut);

        await cut.Find(".sheet-view").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll(".menu-sheet"));
        Assert.Single(cut.FindAll(".concession-menu"));
    }

    // SEC-6, as for a sheet.
    [Fact]
    public async Task PrintMenu_MenuShown_HasNoHrefLinks()
    {
        PrintWindowCloses();
        _sheets.Menu = Drawn(FakeLabelSheets.SnackMenu);
        var cut = Render<StoreManager>();

        await PrintTheMenuAsync(cut);

        Assert.Empty(cut.FindAll("a[href]"));
    }

    // Leaving the screen stops a menu still being drawn. Its cancellation raises nothing, as a sheet's
    // doesn't: ComponentBase ignores an event handler's cancelled task.
    [Fact]
    public async Task Dispose_WhileDrawingTheMenu_CancelsItAndRaisesNothing()
    {
        var drawing = new TaskCompletionSource<MenuDrawing>();
        _sheets.Menu = drawing.Task;
        var cut = Render<StoreManager>();
        var click = Button(cut, "Print the menu").ClickAsync(new MouseEventArgs());
        await DisposeComponentsAsync();

        drawing.SetCanceled(_sheets.LastToken);
        await click;

        Assert.True(_sheets.LastToken.IsCancellationRequested);
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    private static Task<MenuDrawing> Drawn(ConcessionMenu menu) => Task.FromResult(new MenuDrawing(MenuStatus.Drawn, menu));

    // Prints the menu with its button, and waits until its print window has closed.
    private static async Task PrintTheMenuAsync(IRenderedComponent<StoreManager> cut)
    {
        await Button(cut, "Print the menu").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.EndsWith("Print the menu again.", SheetLine(cut), StringComparison.Ordinal));
    }

    // UI-3: the element the screen focused last, from its focus calls so far.
    private void AssertFocusedLast(string? elementId, int focuses)
    {
        var focused = Assert.IsType<ElementReference>(JSInterop.VerifyFocusAsyncInvoke(focuses)[^1].Arguments[0]);
        Assert.Equal(elementId, focused.Id);
    }

    // Completes once the clerk answers the dialog that Shut Down opens.
    private static Task PressShutDownAsync(IRenderedComponent<StoreManager> cut) =>
        cut.FindAll("button").Single(button => button.TextContent == "Shut Down").ClickAsync(new MouseEventArgs());

    // The print window closes as soon as it opens, as when the clerk prints.
    private JSRuntimeInvocationHandler PrintWindowCloses()
    {
        var print = _keyboard.SetupVoid("print");
        print.SetVoidResult();
        return print;
    }

    // Makes a sheet with the button, and waits until its print window has closed.
    private static async Task PrintSheetAsync(IRenderedComponent<StoreManager> cut, string button)
    {
        await Button(cut, button).ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.EndsWith("Press Print this sheet again.", SheetLine(cut), StringComparison.Ordinal));
    }

    private static IElement Button(IRenderedComponent<StoreManager> cut, string text) =>
        cut.FindAll("button").Single(button => button.TextContent == text);

    private static IElement Tab(IRenderedComponent<StoreManager> cut, string name) =>
        cut.FindAll(".store-manager-tabs a").Single(tab => tab.TextContent == name);

    private static string SheetLine(IRenderedComponent<StoreManager> cut) => cut.Find(".sheet-view p").TextContent;

    private static IEnumerable<string> Codes(Sheet sheet) => sheet.Barcodes.Select(barcode => barcode.Code);

    // What a label or card holds, top to bottom: each part's class, or svg for the barcode.
    private static IEnumerable<string> TopToBottom(IElement cell) =>
        cell.Children.Select(part => string.IsNullOrEmpty(part.ClassName) ? part.LocalName : part.ClassName);
}
