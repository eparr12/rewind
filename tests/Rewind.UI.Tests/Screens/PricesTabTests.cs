using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Rewind.Core.StoreManager;
using Rewind.UI.Screens;

namespace Rewind.UI.Tests.Screens;

public sealed class PricesTabTests : BunitContext
{
    private const string Prompt = "Type each price in dollars, such as 2.00, then press Enter.";

    private readonly FakeStorePrices _prices = new();

    public PricesTabTests() => Services.AddSingleton<IStorePrices>(_prices);

    // The boxes show each price in dollars and cents, as Core's Dollars writes them.
    [Fact]
    public void Render_PricesRead_ShowsThemInDollars()
    {
        var cut = Render<PricesTab>();

        Assert.Equal(("2.00", "1.00"), Boxes(cut));
    }

    // UI-6: the tab says it's reading, rather than showing empty boxes.
    [Fact]
    public void Render_WhileReading_SaysSo()
    {
        _prices.Reading = new TaskCompletionSource<Prices>().Task;

        var cut = Render<PricesTab>();

        Assert.Contains(cut.FindAll("p"), p => p.TextContent == "Reading the prices...");
        Assert.Empty(cut.FindAll("form"));
    }

    // UI-6: one line always says what to do.
    [Fact]
    public void Render_PricesRead_SaysWhatToDo()
    {
        var cut = Render<PricesTab>();

        Assert.Equal(Prompt, Notice(cut));
    }

    // UI-3: the Store Manager can type the rental price as soon as the tab opens.
    [Fact]
    public void Render_PricesRead_FocusesTheRentalBox()
    {
        var cut = Render<PricesTab>();

        AssertFocusedLast(BoxId(cut, "#prices-rental"), focuses: 1);
    }

    // ARCH-5: Core reads the boxes as they were typed, so the tab hands them on untouched.
    [Fact]
    public async Task Submit_TypedPrices_SavesThemAsTyped()
    {
        var cut = Render<PricesTab>();
        await cut.Find("#prices-rental").InputAsync("2.5");
        await cut.Find("#prices-late-fee").InputAsync("$1.25");

        await cut.Find("form").SubmitAsync();

        Assert.Equal([("2.5", "$1.25")], _prices.Saves);
    }

    // Saved, the boxes show the prices as Core saved them, and the line says what they are now.
    [Fact]
    public async Task Submit_PricesSaved_ShowsThemAsSaved()
    {
        var cut = Render<PricesTab>();
        await cut.Find("#prices-rental").InputAsync("2.5");
        await cut.Find("#prices-late-fee").InputAsync("$1.25");

        await cut.Find("form").SubmitAsync();

        Assert.Equal(("2.50", "1.25"), Boxes(cut));
        Assert.Equal("Saved. A movie rents for $2.50, and the late fee is $1.25.", Notice(cut));
    }

    // UI-3: the focus goes back to the box the Store Manager was in, so Enter in the late fee's box
    // leaves it there. A save that waits disables the boxes, which takes the focus from them.
    [Theory]
    [InlineData("#prices-rental")]
    [InlineData("#prices-late-fee")]
    public async Task Submit_PricesSaved_FocusesTheBoxItWasIn(string box)
    {
        var cut = Render<PricesTab>();
        var boxId = BoxId(cut, box);
        await cut.Find(box).FocusAsync(new FocusEventArgs());

        await cut.Find("form").SubmitAsync();

        AssertFocusedLast(boxId, focuses: 2);
    }

    [Fact]
    public async Task Submit_RentalPriceNotAPrice_SaysSoAndKeepsWhatWasTyped()
    {
        var cut = Render<PricesTab>();
        await cut.Find("#prices-rental").InputAsync("two");

        await cut.Find("form").SubmitAsync();

        Assert.Equal(
            "The rental price isn't a price. Type it in dollars, from 0.00 to 99.99, such as 2.00. Nothing was saved.",
            Notice(cut));
        Assert.Equal(("two", "1.00"), Boxes(cut));
    }

    // UI-3: the price to fix has the focus, whichever box the Store Manager was in.
    [Fact]
    public async Task Submit_RentalPriceNotAPrice_FocusesItsBox()
    {
        var cut = Render<PricesTab>();
        var rentalBox = BoxId(cut, "#prices-rental");
        await cut.Find("#prices-rental").InputAsync("two");
        await cut.Find("#prices-late-fee").FocusAsync(new FocusEventArgs());

        await cut.Find("form").SubmitAsync();

        AssertFocusedLast(rentalBox, focuses: 2);
    }

    [Fact]
    public async Task Submit_LateFeeNotAPrice_SaysSoAndKeepsWhatWasTyped()
    {
        var cut = Render<PricesTab>();
        await cut.Find("#prices-late-fee").InputAsync("100");

        await cut.Find("form").SubmitAsync();

        Assert.Equal(
            "The late fee isn't a price. Type it in dollars, from 0.00 to 99.99, such as 2.00. Nothing was saved.",
            Notice(cut));
        Assert.Equal(("2.00", "100"), Boxes(cut));
    }

    [Fact]
    public async Task Submit_LateFeeNotAPrice_FocusesItsBox()
    {
        var cut = Render<PricesTab>();
        var lateFeeBox = BoxId(cut, "#prices-late-fee");
        await cut.Find("#prices-late-fee").InputAsync("100");
        await cut.Find("#prices-rental").FocusAsync(new FocusEventArgs());

        await cut.Find("form").SubmitAsync();

        AssertFocusedLast(lateFeeBox, focuses: 2);
    }

    // While Core saves, nothing can be typed or saved again.
    [Fact]
    public async Task Submit_WhileSaving_DisablesTheBoxesAndSave()
    {
        var saving = new TaskCompletionSource<PriceChange>();
        _prices.Saving = saving.Task;
        var cut = Render<PricesTab>();

        var submit = cut.Find("form").SubmitAsync();

        Assert.All(cut.FindAll("input, button"), control => Assert.True(control.HasAttribute("disabled")));
        saving.SetResult(new PriceChange(PriceChangeStatus.Saved, new Prices(200, 100)));
        await submit;
    }

    // A second Enter while Core saves would save again in the middle of the first.
    [Fact]
    public async Task Submit_TwiceWhileSaving_SavesOnce()
    {
        var saving = new TaskCompletionSource<PriceChange>();
        _prices.Saving = saving.Task;
        var cut = Render<PricesTab>();
        var form = cut.Find("form");

        var first = form.SubmitAsync();
        var second = form.SubmitAsync();
        saving.SetResult(new PriceChange(PriceChangeStatus.Saved, new Prices(200, 100)));
        await Task.WhenAll(first, second);

        Assert.Single(_prices.Saves);
    }

    // UI-6: the tab doesn't catch a save that fails. A form handler's exception goes on to the renderer,
    // which hands it to the screen's ErrorBoundary; rendered alone here, it comes back out of the submit.
    [Fact]
    public async Task Submit_SaveFails_LeavesItToTheErrorBoundary()
    {
        _prices.Saving = Task.FromException<PriceChange>(new InvalidOperationException("The save failed."));
        var cut = Render<PricesTab>();

        var submit = () => cut.Find("form").SubmitAsync();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(submit);
        Assert.Equal("The save failed.", thrown.Message);
    }

    // Leaving the tab stops a read still waiting on the database.
    [Fact]
    public async Task Dispose_WhileReading_CancelsTheRead()
    {
        _prices.Reading = new TaskCompletionSource<Prices>().Task;
        _ = Render<PricesTab>();

        await DisposeComponentsAsync();

        Assert.True(_prices.LastToken.IsCancellationRequested);
    }

    // And a save. Its cancellation raises nothing: ComponentBase ignores a form handler's cancelled task
    // (Microsoft Learn, "ASP.NET Core Blazor synchronization context"), so the tab needs no catch for it.
    [Fact]
    public async Task Dispose_WhileSaving_CancelsTheSaveAndRaisesNothing()
    {
        var saving = new TaskCompletionSource<PriceChange>();
        _prices.Saving = saving.Task;
        var cut = Render<PricesTab>();
        var submit = cut.Find("form").SubmitAsync();
        await DisposeComponentsAsync();

        saving.SetCanceled(_prices.LastToken); // as EF Core ends a save whose token was cancelled
        await submit;

        Assert.True(_prices.LastToken.IsCancellationRequested);
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    private static (string Rental, string LateFee) Boxes(IRenderedComponent<PricesTab> cut) =>
        (cut.Find("#prices-rental").GetAttribute("value") ?? "", cut.Find("#prices-late-fee").GetAttribute("value") ?? "");

    private static string Notice(IRenderedComponent<PricesTab> cut) => cut.Find(".prices-prompt").TextContent;

    // The ID of a box's @ref. bUnit writes it into the markup only on the first render, and as "" after
    // that, so a test reads it before anything else happens.
    private static string? BoxId(IRenderedComponent<PricesTab> cut, string selector) =>
        cut.Find(selector).GetAttribute("blazor:elementreference");

    // UI-3: the element the tab focused last, from its focus calls so far.
    private void AssertFocusedLast(string? elementId, int focuses)
    {
        var focused = Assert.IsType<ElementReference>(JSInterop.VerifyFocusAsyncInvoke(focuses)[^1].Arguments[0]);
        Assert.Equal(elementId, focused.Id);
    }
}
