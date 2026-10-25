using Bunit;
using Microsoft.AspNetCore.Components;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class HandEntryFormTests : BunitContext
{
    // UI-3: the clerk can type as soon as the form opens.
    [Fact]
    public void Render_Opens_FocusesTheTitle()
    {
        var cut = Render<HandEntryForm>();

        var focused = Assert.IsType<ElementReference>(JSInterop.VerifyFocusAsyncInvoke().Arguments[0]);
        Assert.Equal(cut.Find("#hand-entry-title").GetAttribute("blazor:elementreference"), focused.Id);
    }

    // The screen redraws with every key typed in its own title box, and gives the form its title
    // each time. Read again, it would put that title back over what the clerk typed here.
    [Fact]
    public async Task Render_ScreenRedraws_KeepsWhatWasTyped()
    {
        var cut = Render<HandEntryForm>(parameters => parameters.Add(form => form.Title, "Halloween"));
        await cut.Find("#hand-entry-title").InputAsync("Halloween II");

        cut.Render(parameters => parameters.Add(form => form.Title, "Hallo"));

        Assert.Equal("Halloween II", cut.Find("#hand-entry-title").GetAttribute("value"));
    }

    // Unknown is blank, which Core keeps as NULL (DATA-3), and the ratings are the ones Core takes.
    [Fact]
    public void Render_Rating_ListsUnknownThenTheUsRatings()
    {
        var cut = Render<HandEntryForm>();

        var options = cut.FindAll("#hand-entry-rating option");

        Assert.Equal(["Unknown", "G", "PG", "PG-13", "R", "NC-17"], options.Select(option => option.TextContent));
        Assert.Equal(["", "G", "PG", "PG-13", "R", "NC-17"], options.Select(option => option.GetAttribute("value")));
    }

    // SEC-5: each box takes no more than its longest answer. Core does the real checking.
    [Fact]
    public void Render_Boxes_CapHowMuchFits()
    {
        var cut = Render<HandEntryForm>();

        var caps = cut.FindAll("input").Select(box => (box.Id, box.GetAttribute("maxlength")));

        Assert.Equal([("hand-entry-title", "200"), ("hand-entry-year", "4"), ("hand-entry-runtime", "3")], caps);
    }
}
