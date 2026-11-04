using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rewind.Core.Concessions;
using Rewind.Core.Devices;
using Rewind.Core.Intake;
using Rewind.Core.Labels;
using Rewind.Core.Members;
using Rewind.Core.Metadata;
using Rewind.Core.Sounds;
using Rewind.UI.Dialogs;
using Rewind.UI.Errors;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests;

public sealed class ShellTests : BunitContext
{
    private readonly DialogService _dialogs;
    private readonly ScannerHub _scanner;
    private readonly ErrorHub _errors;
    private readonly FakeBarcodeTitles _titles = new();
    private readonly FakeMovieIntake _intake = new();
    private readonly FakeMemberRoster _roster = new();
    private readonly ListLogger<Shell> _shellLog = new();
    private readonly ListLogger<ErrorHub> _errorLog = new();
    private readonly CountingBoundaryLogger _boundaryLog = new();

    public ShellTests()
    {
        var sounds = new FakeSoundPlayer();
        var scanner = new ScannerHub(sounds, NullLogger<ScannerHub>.Instance);
        _scanner = scanner;
        _dialogs = new DialogService(sounds);
        _errors = new ErrorHub(_errorLog);

        // The services the shell and its children use, wired as Program.cs wires them.
        Services.AddSingleton(scanner);
        Services.AddSingleton<IScanner>(scanner);
        Services.AddSingleton(_dialogs);
        Services.AddSingleton(_errors);
        Services.AddSingleton<ILogger<Shell>>(_shellLog);
        Services.AddSingleton<IErrorBoundaryLogger>(_boundaryLog);
        Services.AddSingleton<ISoundPlayer>(sounds);
        Services.AddSingleton<IHostApplicationLifetime>(new FakeApplicationLifetime());
        Services.AddSingleton<IMovieIntake>(_intake);
        Services.AddSingleton<IBarcodeTitles>(_titles);
        Services.AddSingleton<ILabelSheets>(new FakeLabelSheets());
        Services.AddSingleton<IMemberRoster>(_roster);
        Services.AddSingleton<IConcessionBar>(new FakeConcessionBar());
        Services.AddSingleton(TimeProvider.System);

        // On its first render, the shell imports keyboard.js and starts its F-key listener, and
        // a dialog that closes hands the focus back through it (UI-3).
        var keyboard = JSInterop.SetupModule("./_content/Rewind.UI/js/keyboard.js");
        keyboard.SetupVoid("start", _ => true).SetVoidResult();
        keyboard.SetupVoid("restoreFocus").SetVoidResult();
    }

    [Fact]
    public async Task OnFunctionKey_F2_OpensReturn()
    {
        var cut = Render<Shell>();

        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F2"));

        Assert.True(cut.HasComponent<Return>());
        Assert.Equal("F2 Return", cut.Find("[role=tab][aria-selected=true]").TextContent);
    }

    [Fact]
    public async Task OnFunctionKey_DialogOpen_StaysOnCheckout()
    {
        var cut = Render<Shell>();
        await cut.InvokeAsync(() => { _ = _dialogs.ShowAsync(new Dialog(Store.Name, "Due back tonight.")); });

        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F2"));

        Assert.True(cut.HasComponent<Checkout>());
    }

    // SEC-6: BlazorWebView opens the default browser for a middle-click on an href link, and
    // MainForm can't stop it, so no screen may have one.
    [Theory]
    [InlineData("F1")]
    [InlineData("F2")]
    [InlineData("F3")]
    [InlineData("F4")]
    [InlineData("F5")]
    public async Task OnFunctionKey_AnyScreen_HasNoHrefLinks(string key)
    {
        var cut = Render<Shell>();

        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey(key));

        Assert.Empty(cut.FindAll("a[href]"));
    }

    // A screen that throws as it opens shows the error dialog instead, and the other screen tests
    // here would still pass on the error's markup.
    [Theory]
    [InlineData("F1")]
    [InlineData("F2")]
    [InlineData("F3")]
    [InlineData("F4")]
    [InlineData("F5")]
    public async Task OnFunctionKey_AnyScreen_OpensWithoutAnError(string key)
    {
        var cut = Render<Shell>();

        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey(key));

        Assert.Null(_dialogs.Current);
    }

    [Fact]
    public void Render_Tabs_AreAllTabStops()
    {
        var cut = Render<Shell>();

        var tabStops = cut.FindAll("[role=tab] a").Count(tab => tab.GetAttribute("tabindex") == "0");

        Assert.Equal(5, tabStops);
    }

    [Fact]
    public async Task KeyDown_EnterOnATab_OpensItsScreen()
    {
        var cut = Render<Shell>();
        var returnTab = cut.FindAll("[role=tab] a").Single(tab => tab.TextContent == "F2 Return");

        await returnTab.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.True(cut.HasComponent<Return>());
    }

    // UI-6: a screen that throws is caught by its ErrorBoundary, which shows the error dialog.
    [Fact]
    public async Task OnFunctionKey_ScreenThrows_ShowsErrorDialog()
    {
        ComponentFactories.Add<Return, ThrowingComponent>();
        var cut = Render<Shell>();

        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F2"));

        Assert.Equal(DialogIcon.Error, _dialogs.Current?.Icon);
    }

    // UI-11: a scan is handled away from Blazor's own events, so what it throws is passed to the
    // screen's ErrorBoundary rather than lost with the task nobody awaits.
    [Fact]
    public async Task Receive_IntakeThrowsOnTheScan_ShowsErrorDialog()
    {
        _titles.Lookup = Task.FromException<BarcodeTitle>(new InvalidOperationException("Something unexpected"));
        var cut = Render<Shell>();
        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F4"));

        _scanner.Receive("032429258373");

        cut.WaitForAssertion(() => Assert.Equal(DialogIcon.Error, _dialogs.Current?.Icon));
    }

    // UI-6, OBS-5: Intake counts the cases on the shelf as it opens (Build Plan 2.6), so a database
    // Rewind can't read fails it at F4, with the error dialog and then Checkout, not at the first scan.
    [Fact]
    public async Task OnFunctionKey_IntakeCantCountTheShelf_ShowsErrorDialogThenCheckout()
    {
        _intake.OnShelf = Task.FromException<int>(new InvalidOperationException("Something unexpected"));
        var cut = Render<Shell>();
        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F4"));
        cut.WaitForAssertion(() => Assert.Equal(DialogIcon.Error, _dialogs.Current?.Icon));

        await cut.InvokeAsync(() => _dialogs.Close(ok: true));

        cut.WaitForAssertion(() => Assert.True(cut.HasComponent<Checkout>()));
    }

    [Fact]
    public async Task CloseErrorDialog_ReturnCrashed_OpensCheckout()
    {
        ComponentFactories.Add<Return, ThrowingComponent>();
        var cut = Render<Shell>();
        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F2"));

        await cut.InvokeAsync(() => _dialogs.Close(ok: true));

        cut.WaitForAssertion(() => Assert.True(cut.HasComponent<Checkout>()));
        Assert.Equal("F1 Checkout", cut.Find("[role=tab][aria-selected=true]").TextContent);
    }

    // Going back to a Checkout that crashes as it opens would bring the error dialog straight
    // back, and the dialog blocks the F-keys, so the clerk could never get away from it.
    [Fact]
    public async Task CloseErrorDialog_CheckoutCrashed_LeavesCheckoutClosed()
    {
        ComponentFactories.Add<Checkout, ThrowingComponent>();
        var cut = Render<Shell>();

        await cut.InvokeAsync(() => _dialogs.Close(ok: true));

        cut.WaitForAssertion(() =>
            Assert.Contains("Checkout closed after an error", cut.Find("[role=tabpanel]").TextContent, StringComparison.Ordinal));
        Assert.Null(_dialogs.Current);
    }

    [Fact]
    public async Task OnFunctionKey_AfterCheckoutCrashed_OpensTheScreen()
    {
        ComponentFactories.Add<Checkout, ThrowingComponent>();
        var cut = Render<Shell>();
        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        cut.WaitForAssertion(() =>
            Assert.Contains("Checkout closed after an error", cut.Find("[role=tabpanel]").TextContent, StringComparison.Ordinal));

        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F5"));

        Assert.True(cut.HasComponent<StoreManager>());
    }

    // A sale lives in Checkout's own fields (ADR-0003), and an error from outside the screen ends on
    // Checkout, so that must leave the Checkout on screen in place, not start another.
    [Fact]
    public async Task CloseErrorDialog_WinFormsErrorOverCheckout_KeepsTheSameCheckout()
    {
        var cut = Render<Shell>();
        var checkout = cut.FindComponent<Checkout>().Instance;
        await cut.InvokeAsync(() => _errors.Report(new InvalidOperationException("Test crash")));

        await CloseDialogAsync(cut);

        Assert.Same(checkout, cut.FindComponent<Checkout>().Instance);
    }

    [Fact]
    public async Task CloseErrorDialog_ErrorFromAComponentOverCheckout_KeepsTheSameCheckout()
    {
        var cut = Render<Shell>();
        var checkout = cut.FindComponent<Checkout>().Instance;
        await cut.InvokeAsync(() => _errors.ReportFromComponent(nameof(Intake), new InvalidOperationException("Test crash")));

        await CloseDialogAsync(cut);

        Assert.Same(checkout, cut.FindComponent<Checkout>().Instance);
    }

    [Fact]
    public async Task OnFunctionKey_F1OnCheckout_KeepsTheSameCheckout()
    {
        var cut = Render<Shell>();
        var checkout = cut.FindComponent<Checkout>().Instance;

        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F1"));

        Assert.Same(checkout, cut.FindComponent<Checkout>().Instance);
    }

    // Leaving Checkout is what drops a sale, as Intake drops a case (ADR-0005).
    [Fact]
    public async Task OnFunctionKey_LeavingCheckoutAndComingBack_OpensAnotherCheckout()
    {
        var cut = Render<Shell>();
        var checkout = cut.FindComponent<Checkout>().Instance;
        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F2"));

        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F1"));

        Assert.NotSame(checkout, cut.FindComponent<Checkout>().Instance);
    }

    // What the bar throws closes the bar and nothing else, so it can't cost the clerk a sale. App's
    // ErrorBoundary, the next one up, would replace the whole shell.
    [Fact]
    public async Task Click_StatusBarThrows_ShowsOneLineInItsPlace()
    {
        ComponentFactories.Add<StatusBar, ThrowsOnClick>();
        var cut = Render<Shell>();

        await cut.Find("button.throws").ClickAsync(new MouseEventArgs());

        Assert.Equal(["Status bar closed after an error. Tell the Store Manager."], cut.FindAll(".status-bar-field").Select(field => field.TextContent));
    }

    [Fact]
    public async Task Click_StatusBarThrows_ShowsErrorDialog()
    {
        ComponentFactories.Add<StatusBar, ThrowsOnClick>();
        var cut = Render<Shell>();

        await cut.Find("button.throws").ClickAsync(new MouseEventArgs());

        Assert.Equal(DialogIcon.Error, _dialogs.Current?.Icon);
    }

    // OBS-3, OBS-4: one line, naming the bar, and none from ErrorBoundary's own logger.
    [Fact]
    public async Task Click_StatusBarThrows_LogsItOnce()
    {
        ComponentFactories.Add<StatusBar, ThrowsOnClick>();
        var cut = Render<Shell>();

        await cut.Find("button.throws").ClickAsync(new MouseEventArgs());

        Assert.Equal(["Unhandled exception from StatusBar; reported to the shell"], _errorLog.Entries.Select(entry => entry.Message));
        Assert.Equal(0, _boundaryLog.Count);
    }

    [Fact]
    public async Task CloseErrorDialog_StatusBarCrashed_KeepsTheSameCheckout()
    {
        ComponentFactories.Add<StatusBar, ThrowsOnClick>();
        var cut = Render<Shell>();
        var checkout = cut.FindComponent<Checkout>().Instance;
        await cut.Find("button.throws").ClickAsync(new MouseEventArgs());

        await CloseDialogAsync(cut);

        Assert.Same(checkout, cut.FindComponent<Checkout>().Instance);
    }

    // OBS-5: recovery never reopens what just crashed, so a bar that can't draw doesn't bring the error
    // dialog back at each F-key, as a screen's boundary does when it's the screen the clerk asked for.
    [Fact]
    public async Task OnFunctionKey_AfterStatusBarCrashed_LeavesItClosed()
    {
        ComponentFactories.Add<StatusBar, ThrowsOnClick>();
        var cut = Render<Shell>();
        await cut.Find("button.throws").ClickAsync(new MouseEventArgs());
        await CloseDialogAsync(cut);

        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F2"));

        Assert.Empty(cut.FindAll("button.throws"));
    }

    // The real bar, and what #48 feared: a status it can't draw. StateHasChanged only queues the drawing,
    // so nothing in the bar could catch this, and Blazor hands it to the nearest ErrorBoundary.
    [Fact]
    public async Task StatusChanged_BarCantDrawTheStatus_ShowsOneLineInItsPlace()
    {
        var scanner = new FakeScanner();
        Services.AddSingleton<IScanner>(scanner);
        var cut = Render<Shell>();

        await cut.InvokeAsync(() => scanner.SetStatusThatFails(new InvalidOperationException("Test crash")));

        Assert.Equal(["Status bar closed after an error. Tell the Store Manager."], cut.FindAll(".status-bar-field").Select(field => field.TextContent));
    }

    // A failed lookup isn't the bar failing, so it stays open, still saying what was scanned, and the next
    // scan looks again.
    [Fact]
    public async Task Receive_StatusBarLookupFails_LeavesTheBarOpen()
    {
        _roster.Finding = Task.FromException<MemberRow?>(new InvalidOperationException("The lookup failed."));
        var cut = Render<Shell>();

        await cut.InvokeAsync(() => _scanner.Receive("MEM-001"));

        Assert.Equal("MEM-001 · Member card", cut.Find(".status-bar-field").TextContent);
    }

    // OBS-3: a line for each screen change, so the lines around an error say where the clerk was.
    [Fact]
    public async Task OnFunctionKey_AnotherScreen_LogsItOpening()
    {
        var cut = Render<Shell>();

        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F2"));

        Assert.Equal([(LogLevel.Information, "Opened Return", (Exception?)null)], _shellLog.Entries);
    }

    [Fact]
    public async Task KeyDown_EnterOnATab_LogsItOpening()
    {
        var cut = Render<Shell>();
        var returnTab = cut.FindAll("[role=tab] a").Single(tab => tab.TextContent == "F2 Return");

        await returnTab.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(["Opened Return"], LoggedLines);
    }

    [Fact]
    public async Task OnFunctionKey_F1OnCheckout_LogsNothing()
    {
        var cut = Render<Shell>();

        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F1"));

        Assert.Empty(LoggedLines);
    }

    // "Opened Checkout" here would be false: the clerk never left the Checkout they had.
    [Fact]
    public async Task CloseErrorDialog_ErrorReportedOverCheckout_LogsNothing()
    {
        var cut = Render<Shell>();
        await cut.InvokeAsync(() => _errors.Report(new InvalidOperationException("Test crash")));

        await CloseDialogAsync(cut);

        Assert.Empty(LoggedLines);
    }

    [Fact]
    public async Task CloseErrorDialog_ErrorReportedOverAnotherScreen_LogsCheckoutOpening()
    {
        var cut = Render<Shell>();
        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F2"));
        await cut.InvokeAsync(() => _errors.Report(new InvalidOperationException("Test crash")));

        await CloseDialogAsync(cut);

        Assert.Equal(["Opened Return", "Opened Checkout"], LoggedLines);
    }

    [Fact]
    public async Task CloseErrorDialog_ReturnCrashed_LogsCheckoutOpening()
    {
        ComponentFactories.Add<Return, ThrowingComponent>();
        var cut = Render<Shell>();
        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F2"));

        await CloseDialogAsync(cut);

        Assert.Equal(["Opened Return", "Opened Checkout"], LoggedLines);
    }

    // Checkout stays closed after its own crash (OBS-5), so nothing has opened yet.
    [Fact]
    public async Task CloseErrorDialog_CheckoutCrashed_LogsNothing()
    {
        ComponentFactories.Add<Checkout, ThrowingComponent>();
        var cut = Render<Shell>();

        await CloseDialogAsync(cut);

        Assert.Empty(LoggedLines);
    }

    // F1 on a Checkout that closed after its crash is a new one, on the same tab.
    [Fact]
    public async Task OnFunctionKey_F1AfterCheckoutCrashed_LogsItOpening()
    {
        ComponentFactories.Add<Checkout, ThrowingComponent>();
        var cut = Render<Shell>();
        await CloseDialogAsync(cut);

        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F1"));

        Assert.Equal(["Opened Checkout"], LoggedLines);
    }

    private IEnumerable<string> LoggedLines => _shellLog.Entries.Select(entry => entry.Message);

    // Closes the dialog on screen, then a turn of the renderer's thread, so what its closing sets off has
    // run before the test looks.
    private async Task CloseDialogAsync(IRenderedComponent<Shell> cut)
    {
        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        await cut.InvokeAsync(() => { });
    }

    private sealed class CountingBoundaryLogger : IErrorBoundaryLogger
    {
        public int Count { get; private set; }

        public ValueTask LogErrorAsync(Exception exception)
        {
            Count++;
            return ValueTask.CompletedTask;
        }
    }
}
