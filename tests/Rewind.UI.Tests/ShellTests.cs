using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly FakeBarcodeTitles _titles = new();
    private readonly FakeMovieIntake _intake = new();

    public ShellTests()
    {
        var sounds = new FakeSoundPlayer();
        var scanner = new ScannerHub(sounds, NullLogger<ScannerHub>.Instance);
        _scanner = scanner;
        _dialogs = new DialogService(sounds);

        // The services the shell and its children use, wired as Program.cs wires them.
        Services.AddSingleton(scanner);
        Services.AddSingleton<IScanner>(scanner);
        Services.AddSingleton(_dialogs);
        Services.AddSingleton(new ErrorHub(NullLogger<ErrorHub>.Instance));
        Services.AddSingleton<ISoundPlayer>(sounds);
        Services.AddSingleton<IHostApplicationLifetime>(new FakeApplicationLifetime());
        Services.AddSingleton<IMovieIntake>(_intake);
        Services.AddSingleton<IBarcodeTitles>(_titles);
        Services.AddSingleton<ILabelSheets>(new FakeLabelSheets());
        Services.AddSingleton<IMemberRoster>(new FakeMemberRoster());
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
}
