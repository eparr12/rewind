using Rewind.Core.Sounds;
using Rewind.UI.Dialogs;

namespace Rewind.UI.Tests.Dialogs;

public sealed class DialogServiceTests
{
    private static readonly Dialog Question = new(Store.Name, "Rewind the tape?", DialogIcon.Warning, CanCancel: true);
    private static readonly Dialog Note = new(Store.Name, "Due back tonight.");

    private readonly FakeSoundPlayer _sounds = new();

    [Fact]
    public async Task ShowAsync_ClerkPressesOk_ReturnsTrue()
    {
        var dialogs = new DialogService(_sounds);
        var answer = dialogs.ShowAsync(Question);

        dialogs.Close(ok: true);

        Assert.True(await answer);
    }

    [Fact]
    public async Task ShowAsync_ClerkCancels_ReturnsFalse()
    {
        var dialogs = new DialogService(_sounds);
        var answer = dialogs.ShowAsync(Question);

        dialogs.Close(ok: false);

        Assert.False(await answer);
    }

    [Fact]
    public void Close_OpenDialog_LeavesNothingOnScreen()
    {
        var dialogs = new DialogService(_sounds);
        _ = dialogs.ShowAsync(Note);

        dialogs.Close(ok: true);

        Assert.Null(dialogs.Current);
    }

    [Fact]
    public async Task ShowAsync_WhileAnotherIsOpen_CancelsTheFirst()
    {
        var dialogs = new DialogService(_sounds);
        var first = dialogs.ShowAsync(Question);

        var second = dialogs.ShowAsync(Note);

        Assert.False(await first);
        Assert.Same(Note, dialogs.Current);
        Assert.False(second.IsCompleted);
    }

    [Fact]
    public void ShowAsync_Warning_PlaysErrorChord()
    {
        var dialogs = new DialogService(_sounds);

        _ = dialogs.ShowAsync(Question);

        Assert.Equal(Sound.Error, Assert.Single(_sounds.Played));
    }

    [Fact]
    public void ShowAsync_Info_PlaysNothing()
    {
        var dialogs = new DialogService(_sounds);

        _ = dialogs.ShowAsync(Note);

        Assert.Empty(_sounds.Played);
    }

    [Fact]
    public void ShowAsync_AnyDialog_RaisesChanged()
    {
        var dialogs = new DialogService(_sounds);
        var changes = 0;
        dialogs.Changed += () => changes++;

        _ = dialogs.ShowAsync(Note);

        Assert.Equal(1, changes);
    }
}
