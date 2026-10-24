using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Rewind.Core.Devices;
using Rewind.Core.Sounds;
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
        Services.AddSingleton<ISoundPlayer>(sounds);
        Services.AddSingleton<IHostApplicationLifetime>(new FakeApplicationLifetime());

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
}
