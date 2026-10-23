using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rewind.Core.Devices;
using Rewind.UI.Dialogs;
using Rewind.UI.Errors;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests;

public sealed class ShellTests : BunitContext
{
    private readonly DialogService _dialogs;

    public ShellTests()
    {
        var sounds = new FakeSoundPlayer();
        var scanner = new ScannerHub(sounds, NullLogger<ScannerHub>.Instance);
        _dialogs = new DialogService(sounds);

        // The services the shell and its children use, wired as Program.cs wires them.
        Services.AddSingleton(scanner);
        Services.AddSingleton<IScanner>(scanner);
        Services.AddSingleton(_dialogs);
        Services.AddSingleton(new ErrorHub(NullLogger<ErrorHub>.Instance));

        // On its first render, the shell imports keyboard.js and starts its F-key listener (UI-3).
        JSInterop.SetupModule("./_content/Rewind.UI/js/keyboard.js")
            .SetupVoid("start", _ => true)
            .SetVoidResult();
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
        await cut.InvokeAsync(() => { _ = _dialogs.ShowAsync(new Dialog("Rewind Video", "Due back tonight.")); });

        await cut.InvokeAsync(() => cut.Instance.OnFunctionKey("F2"));

        Assert.True(cut.HasComponent<Checkout>());
    }
}
