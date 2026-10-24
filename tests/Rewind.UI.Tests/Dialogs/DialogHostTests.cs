using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Rewind.UI.Dialogs;

namespace Rewind.UI.Tests.Dialogs;

public sealed class DialogHostTests : BunitContext
{
    private static readonly Dialog Question = new(Store.Name, "Rewind the tape?", DialogIcon.Warning, CanCancel: true);
    private static readonly Dialog Note = new(Store.Name, "Due back tonight.");

    private readonly DialogService _dialogs = new(new FakeSoundPlayer());
    private readonly BunitJSModuleInterop _keyboard;

    public DialogHostTests()
    {
        Services.AddSingleton(_dialogs);

        // A dialog that closes hands the focus back through keyboard.js (UI-3).
        _keyboard = JSInterop.SetupModule("./_content/Rewind.UI/js/keyboard.js");
        _keyboard.SetupVoid("restoreFocus").SetVoidResult();
    }

    [Fact]
    public async Task ClickOk_AnyDialog_AnswersOk()
    {
        var (host, answer) = Show(Question);

        await host.Find("button.default").ClickAsync(new MouseEventArgs());

        Assert.True(await answer);
    }

    [Fact]
    public async Task ClickCancel_DialogWithCancel_AnswersCancel()
    {
        var (host, answer) = Show(Question);

        await CancelButton(host).ClickAsync(new MouseEventArgs());

        Assert.False(await answer);
    }

    [Fact]
    public void Render_DialogWithCancel_HasCancelButton()
    {
        var (host, _) = Show(Question);

        Assert.Contains(host.FindAll(".dialog-buttons button"), button => button.TextContent == "Cancel");
    }

    [Fact]
    public void Render_OkOnlyDialog_HasNoCancelButton()
    {
        var (host, _) = Show(Note);

        Assert.Equal("OK", Assert.Single(host.FindAll(".dialog-buttons button")).TextContent);
    }

    [Fact]
    public async Task KeyDownEscape_DialogWithCancel_AnswersCancel()
    {
        var (host, answer) = Show(Question);

        await host.Find(".dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.False(await answer);
    }

    // As in Windows 98, Esc on a message box with only OK answers OK.
    [Fact]
    public async Task KeyDownEscape_OkOnlyDialog_AnswersOk()
    {
        var (host, answer) = Show(Note);

        await host.Find(".dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(await answer);
    }

    [Fact]
    public async Task ClickClose_DialogWithCancel_AnswersCancel()
    {
        var (host, answer) = Show(Question);

        await host.Find("[aria-label=Close]").ClickAsync(new MouseEventArgs());

        Assert.False(await answer);
    }

    [Fact]
    public async Task ClickClose_OkOnlyDialog_AnswersOk()
    {
        var (host, answer) = Show(Note);

        await host.Find("[aria-label=Close]").ClickAsync(new MouseEventArgs());

        Assert.True(await answer);
    }

    // UI-3: a dialog opens with OK focused, so Enter presses it.
    [Fact]
    public void Render_NewDialog_FocusesOk()
    {
        _ = Show(Note);

        JSInterop.VerifyFocusAsyncInvoke();
    }

    // UI-3: once it closes, the focus goes back to where the clerk was.
    [Fact]
    public async Task ClickOk_AnyDialog_GivesTheFocusBack()
    {
        var (host, _) = Show(Note);

        await host.Find("button.default").ClickAsync(new MouseEventArgs());

        host.WaitForAssertion(() => _keyboard.VerifyInvoke("restoreFocus"));
    }

    private (IRenderedComponent<DialogHost> Host, Task<bool> Answer) Show(Dialog dialog)
    {
        var answer = _dialogs.ShowAsync(dialog);
        return (Render<DialogHost>(), answer);
    }

    private static IElement CancelButton(IRenderedComponent<DialogHost> host) =>
        host.FindAll(".dialog-buttons button").Single(button => button.TextContent == "Cancel");
}
