using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rewind.Core.Sounds;
using Rewind.UI.Dialogs;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class StoreManagerTests : BunitContext
{
    private readonly FakeApplicationLifetime _lifetime = new();
    private readonly FakeSoundPlayer _sounds = new();
    private readonly DialogService _dialogs;

    public StoreManagerTests()
    {
        _dialogs = new DialogService(_sounds);

        Services.AddSingleton<IHostApplicationLifetime>(_lifetime);
        Services.AddSingleton(_dialogs);
        Services.AddSingleton<ISoundPlayer>(_sounds); // Debug builds add Diagnostics, which plays sounds
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

    // Completes once the clerk answers the dialog that Shut Down opens.
    private static Task PressShutDownAsync(IRenderedComponent<StoreManager> cut) =>
        cut.FindAll("button").Single(button => button.TextContent == "Shut Down").ClickAsync(new MouseEventArgs());
}
