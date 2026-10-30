using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Rewind.Core.Concessions;
using Rewind.Core.Devices;
using Rewind.Core.Members;
using Rewind.Core.Sounds;
using Rewind.UI.Dialogs;
using Rewind.UI.Errors;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests;

// OBS-5: in BlazorWebView, an exception that no ErrorBoundary catches stops the whole page, so
// App's boundary has to catch whatever the shell throws outside its screens. The status bar
// stands in here for any such part of the shell.
public sealed class AppTests : BunitContext
{
    private readonly DialogService _dialogs;
    private readonly ScannerHub _scanner;
    private readonly FakeMemberRoster _roster = new();
    private readonly FakeApplicationLifetime _lifetime = new();

    public AppTests()
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
        Services.AddSingleton<IHostApplicationLifetime>(_lifetime);
        Services.AddSingleton<IMemberRoster>(_roster);
        Services.AddSingleton<IConcessionBar>(new FakeConcessionBar());

        // Each shell starts keyboard.js after its first render, and a dialog that closes hands
        // the focus back through it (UI-3).
        var keyboard = JSInterop.SetupModule("./_content/Rewind.UI/js/keyboard.js");
        keyboard.SetupVoid("start", _ => true).SetVoidResult();
        keyboard.SetupVoid("restoreFocus").SetVoidResult();
    }

    [Fact]
    public async Task Click_PartOfTheShellThrows_ShowsErrorDialog()
    {
        ComponentFactories.Add<StatusBar, ThrowsOnClick>();
        var cut = Render<App>();

        await cut.Find("button.throws").ClickAsync(new MouseEventArgs());

        Assert.Equal(DialogIcon.Error, _dialogs.Current?.Icon);
    }

    [Fact]
    public async Task CloseErrorDialog_ShellCrashedAfterStarting_OpensNewShellOnCheckout()
    {
        ComponentFactories.Add<StatusBar, ThrowsOnClick>();
        var cut = Render<App>();
        await cut.InvokeAsync(() => cut.FindComponent<Shell>().Instance.OnFunctionKey("F2"));
        await cut.Find("button.throws").ClickAsync(new MouseEventArgs());

        await cut.InvokeAsync(() => _dialogs.Close(ok: true));

        cut.WaitForAssertion(() => Assert.True(cut.HasComponent<Checkout>()));
        Assert.False(_lifetime.StopRequested);
    }

    // A new shell would crash the same way and bring the error dialog back each time, so Rewind
    // shuts down rather than leave the clerk closing it forever.
    [Fact]
    public async Task CloseErrorDialog_ShellCrashedWhileStarting_ShutsRewindDown()
    {
        ComponentFactories.Add<StatusBar, ThrowingComponent>();
        var cut = Render<App>();

        await cut.InvokeAsync(() => _dialogs.Close(ok: true));

        cut.WaitForAssertion(() => Assert.True(_lifetime.StopRequested));
    }

    // The shell's DialogHost throws, and so does the one in App's error content. Drawing the error
    // dialog again would only throw again, until BlazorWebView stopped the page.
    [Fact]
    public void Render_ErrorDialogThrowsToo_ShowsPlainNotice()
    {
        ComponentFactories.Add<DialogHost, ThrowingComponent>();

        var cut = Render<App>();

        Assert.Contains("Rewind can't recover from this error", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClickShutDown_ErrorDialogThrewToo_ShutsRewindDown()
    {
        ComponentFactories.Add<DialogHost, ThrowingComponent>();
        var cut = Render<App>();

        await cut.Find("button.default").ClickAsync(new MouseEventArgs());

        Assert.True(_lifetime.StopRequested);
    }

    // UI-3: Enter presses Shut Down. autofocus needs no JavaScript interop, which may have broken.
    [Fact]
    public void Render_ErrorDialogThrowsToo_FocusesShutDown()
    {
        ComponentFactories.Add<DialogHost, ThrowingComponent>();

        var cut = Render<App>();

        Assert.True(cut.Find("button.default").HasAttribute("autofocus"));
    }

    // Only an exception raised while the error dialog is up means the dialog itself broke. Once
    // the clerk closes it and a new shell starts, a later crash gets the full dialog again.
    [Fact]
    public async Task Click_ShellCrashesAgainAfterRecovering_ShowsFullErrorDialog()
    {
        ComponentFactories.Add<StatusBar, ThrowsOnClick>();
        var cut = Render<App>();
        await cut.Find("button.throws").ClickAsync(new MouseEventArgs());
        await cut.InvokeAsync(() => _dialogs.Close(ok: true));
        cut.WaitForAssertion(() => Assert.True(cut.HasComponent<Shell>()));

        await cut.Find("button.throws").ClickAsync(new MouseEventArgs());

        Assert.Equal(DialogIcon.Error, _dialogs.Current?.Icon);
        Assert.DoesNotContain("can't recover", cut.Markup, StringComparison.Ordinal);
    }

    // The status bar's lookup failing shows the error dialog over the shell it's in, which carries on.
    // Handed to this boundary, it would replace the whole shell, or shut Rewind down while it started.
    [Fact]
    public async Task Receive_StatusBarLookupFails_ShowsErrorDialogOverTheSameShell()
    {
        _roster.Finding = Task.FromException<MemberRow?>(new InvalidOperationException("The lookup failed."));
        var cut = Render<App>();
        var shell = cut.FindComponent<Shell>().Instance;

        await cut.InvokeAsync(() => _scanner.Receive("MEM-001"));

        Assert.Equal(DialogIcon.Error, _dialogs.Current?.Icon);
        Assert.Same(shell, cut.FindComponent<Shell>().Instance);
    }
}
