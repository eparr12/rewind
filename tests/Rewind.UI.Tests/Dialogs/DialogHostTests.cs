using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Rewind.UI.Dialogs;

namespace Rewind.UI.Tests.Dialogs;

public sealed class DialogHostTests : BunitContext
{
    private static readonly Dialog Question = new(Store.Name, "Rewind the tape?", DialogIcon.Warning, CanCancel: true);
    private static readonly Dialog Note = new(Store.Name, "Due back tonight.");

    // What a Close Out might report, with the copy to the USB drive, the third of four lines, not done.
    private static readonly ChecklistLine[] CloseOutLines =
    [
        new("The night is closed.", Ticked: true),
        new("Tonight's backup is saved.", Ticked: true),
        new("The backup is copied to the USB drive.", Ticked: false),
        new("The phone catalog is written.", Ticked: true),
    ];

    private static readonly Dialog Report = new(Store.Name, "Close Out is done.") { Checklist = CloseOutLines };

    private readonly DialogService _dialogs = new(new FakeSoundPlayer());
    private readonly BunitJSModuleInterop _keyboard;

    public DialogHostTests()
    {
        Services.AddSingleton(_dialogs);

        // A dialog that closes hands the focus back through keyboard.js (UI-3).
        _keyboard = JSInterop.SetupModule("./_content/Rewind.UI/js/keyboard.js");
        _keyboard.SetupVoid("restoreFocus").SetVoidResult();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClickOk_AnyDialog_AnswersOk(bool checklist)
    {
        var (host, answer) = Show(WithChecklistIf(checklist, Question));

        await host.Find("button.default").ClickAsync(new MouseEventArgs());

        Assert.True(await answer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClickCancel_DialogWithCancel_AnswersCancel(bool checklist)
    {
        var (host, answer) = Show(WithChecklistIf(checklist, Question));

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyDownEscape_DialogWithCancel_AnswersCancel(bool checklist)
    {
        var (host, answer) = Show(WithChecklistIf(checklist, Question));

        await host.Find(".dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.False(await answer);
    }

    // As in Windows 98, Esc on a message box with only OK answers OK.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyDownEscape_OkOnlyDialog_AnswersOk(bool checklist)
    {
        var (host, answer) = Show(WithChecklistIf(checklist, Note));

        await host.Find(".dialog").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(await answer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClickClose_DialogWithCancel_AnswersCancel(bool checklist)
    {
        var (host, answer) = Show(WithChecklistIf(checklist, Question));

        await host.Find("[aria-label=Close]").ClickAsync(new MouseEventArgs());

        Assert.False(await answer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClickClose_OkOnlyDialog_AnswersOk(bool checklist)
    {
        var (host, answer) = Show(WithChecklistIf(checklist, Note));

        await host.Find("[aria-label=Close]").ClickAsync(new MouseEventArgs());

        Assert.True(await answer);
    }

    // UI-3: a dialog opens with OK focused, so Enter presses it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Render_NewDialog_FocusesOk(bool checklist)
    {
        var (host, _) = Show(WithChecklistIf(checklist, Question));

        var focused = Assert.IsType<ElementReference>(JSInterop.VerifyFocusAsyncInvoke().Arguments[0]);
        Assert.Equal(host.Find("button.default").GetAttribute("blazor:elementreference"), focused.Id);
    }

    // UI-3: once it closes, the focus goes back to where the clerk was.
    [Fact]
    public async Task ClickOk_AnyDialog_GivesTheFocusBack()
    {
        var (host, _) = Show(Note);

        await host.Find("button.default").ClickAsync(new MouseEventArgs());

        host.WaitForAssertion(() => _keyboard.VerifyInvoke("restoreFocus"));
    }

    [Fact]
    public void Render_Checklist_ShowsEachLineInOrderWithItsTick()
    {
        var (host, _) = Show(Report);

        Assert.Equal(CloseOutLines, Lines(host));
    }

    [Fact]
    public void Render_Checklist_TiesEachBoxToItsOwnLabel()
    {
        var (host, _) = Show(Report);

        // 98.css spaces each row, and draws each box on the label right after it.
        var rows = host.FindAll(".dialog-checklist > .field-row");
        Assert.Equal(CloseOutLines.Length, rows.Count);
        Assert.All(rows, row => Assert.Collection(
            row.Children,
            box => Assert.True(box.Matches("input[type=checkbox]")),
            label => Assert.Equal(row.Children[0].Id, label.GetAttribute("for"))));
        Assert.Distinct(rows.Select(row => row.Children[0].Id));
    }

    [Fact]
    public void Render_Checklist_DrawsItBetweenTheMessageAndTheButtons()
    {
        var (host, _) = Show(Report);

        Assert.Equal(
            ["dialog-message", "dialog-checklist", "dialog-buttons"],
            host.Find(".window-body").Children.Select(child => child.ClassName));
    }

    // A dialog without a checklist draws only its message and buttons, as before there were checklists.
    [Fact]
    public void Render_NoChecklist_HasNoCheckbox()
    {
        var (host, _) = Show(Note);

        Assert.Empty(host.FindAll("input[type=checkbox]"));
        Assert.Equal(
            ["dialog-message", "dialog-buttons"],
            host.Find(".window-body").Children.Select(child => child.ClassName));
    }

    // The edge of "a dialog with a checklist": a single line still gets one.
    [Fact]
    public void Render_OneLineChecklist_ShowsTheLine()
    {
        ChecklistLine copy = new("The backup is copied to the USB drive.", Ticked: false);

        var (host, _) = Show(Note with { Checklist = [copy] });

        Assert.Equal([copy], Lines(host));
    }

    // 98.css greys the words of a box that's disabled or read-only, and these are lines a kid has to read.
    [Fact]
    public void Render_Checklist_GreysNoLine()
    {
        var (host, _) = Show(Report);

        Assert.All(host.FindAll(".dialog-checklist input"), box =>
        {
            Assert.False(box.HasAttribute("disabled"));
            Assert.False(box.HasAttribute("readonly"));
        });
    }

    // In the browser, a click on a box or its words ticks or unticks it, and so does Space, by clicking the box.
    // bUnit has no browser to do that, so this shows what stops it: nothing in Rewind hears the click, and the
    // checklist cancels it.
    [Theory]
    [InlineData("input")]
    [InlineData("label")]
    public async Task Click_ChecklistBoxOrWords_ChangesNothing(string part)
    {
        var (host, answer) = Show(Report);

        foreach (var element in host.FindAll($".dialog-checklist {part}"))
        {
            await Assert.ThrowsAsync<MissingEventHandlerException>(() => element.ClickAsync(new MouseEventArgs()));
            Assert.True(CancelsClick(element));
        }

        Assert.Equal(CloseOutLines, Lines(host));
        Assert.False(answer.IsCompleted);
    }

    // No box takes the focus, but a Space that reached one would only meet the dialog's own keys, which answer
    // to Esc alone. The click that Space makes is cancelled, as Click_ChecklistBoxOrWords_ChangesNothing shows.
    [Fact]
    public async Task KeyDownSpace_OnABox_ChangesNothing()
    {
        var (host, answer) = Show(Report);

        foreach (var box in host.FindAll(".dialog-checklist input"))
        {
            await box.KeyDownAsync(new KeyboardEventArgs { Key = " ", Code = "Space" });
        }

        Assert.Equal(CloseOutLines, Lines(host));
        Assert.False(answer.IsCompleted);
    }

    // UI-3: in the browser, a press of the mouse button moves the focus, which would take it off OK, or off Cancel
    // once the clerk has tabbed to it. Nothing in Rewind hears the press, and the dialog cancels it, on the dialog
    // and on the screen behind it.
    [Theory]
    [InlineData("message")]
    [InlineData("words")]
    [InlineData("box")]
    [InlineData("title")]
    [InlineData("behind")]
    public async Task MouseDown_OnOrBehindTheDialog_LeavesTheFocusWhereItIs(string place)
    {
        var (host, answer) = Show(Report);
        var pressed = host.Find(Place(place));

        await Assert.ThrowsAsync<MissingEventHandlerException>(() => pressed.MouseDownAsync(new MouseEventArgs()));

        Assert.True(CancelsPress(pressed));
        Assert.False(answer.IsCompleted);
    }

    // UI-3: keyboard.js's keepTabInDialog sends Tab round the dialog's Tab stops, so no box is one, and OK keeps
    // the focus it opens with.
    [Fact]
    public void Render_Checklist_TabGoesRoundTheButtonsOnly()
    {
        var (host, _) = Show(Question with { Checklist = CloseOutLines });

        var stops = host.FindAll(".dialog button, .dialog input, .dialog select, .dialog textarea").Where(IsTabStop);

        Assert.Equal(["OK", "Cancel"], stops.Select(control => control.TextContent));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShowAsync_TwoChecklistsOneAfterTheOther_ShareNoIds(bool firstClosed)
    {
        var (host, _) = Show(Report);
        var first = Ids(host);
        if (firstClosed)
        {
            await host.Find("button.default").ClickAsync(new MouseEventArgs());
        }

        _ = _dialogs.ShowAsync(Report with { Message = "Close Out is done again." });

        var second = Ids(host);
        Assert.NotEmpty(second);
        Assert.Empty(first.Intersect(second));
    }

    private (IRenderedComponent<DialogHost> Host, Task<bool> Answer) Show(Dialog dialog)
    {
        var answer = _dialogs.ShowAsync(dialog);
        return (Render<DialogHost>(), answer);
    }

    private static Dialog WithChecklistIf(bool checklist, Dialog dialog) =>
        checklist ? dialog with { Checklist = CloseOutLines } : dialog;

    private static IElement CancelButton(IRenderedComponent<DialogHost> host) =>
        host.FindAll(".dialog-buttons button").Single(button => button.TextContent == "Cancel");

    // The checklist as the clerk reads it: each label's words, and whether the box the label is tied to is ticked.
    private static ChecklistLine[] Lines(IRenderedComponent<DialogHost> host) =>
    [
        .. host.FindAll(".dialog-checklist label").Select(label =>
            new ChecklistLine(label.TextContent, host.Find($"#{label.GetAttribute("for")}").HasAttribute("checked"))),
    ];

    // Every id in the checklist, its boxes' and the ones its labels name.
    private static string[] Ids(IRenderedComponent<DialogHost> host) =>
    [
        .. host.FindAll(".dialog-checklist input").Select(box => box.Id ?? ""),
        .. host.FindAll(".dialog-checklist label").Select(label => label.GetAttribute("for") ?? ""),
    ];

    // In the browser, Blazor cancels a click when the element, or one around it, has @onclick:preventDefault, which
    // bUnit writes as this attribute.
    private static bool CancelsClick(IElement element) =>
        element.Closest("[blazor\\:onclick\\:preventdefault]") is not null;

    // Blazor cancels a press as it does a click: when the element, or one around it, has @onmousedown:preventDefault.
    private static bool CancelsPress(IElement element) =>
        element.Closest("[blazor\\:onmousedown\\:preventdefault]") is not null;

    // Where the clerk might press: the message, a line's words, its box, the title bar's words, or the screen behind
    // the dialog, which the dialog's layer covers.
    private static string Place(string place) => place switch
    {
        "message" => "#dialog-message",
        "words" => ".dialog-checklist label",
        "box" => ".dialog-checklist input",
        "title" => "#dialog-title",
        "behind" => ".dialog-layer",
        _ => throw new ArgumentOutOfRangeException(nameof(place), place, "No such place in the dialog."),
    };

    // As keyboard.js's keepTabInDialog picks them: a control that isn't disabled, with a tabindex of 0 or more,
    // which a control without one has.
    private static bool IsTabStop(IElement control) =>
        !control.HasAttribute("disabled")
        && int.Parse(control.GetAttribute("tabindex") ?? "0", CultureInfo.InvariantCulture) >= 0;
}
