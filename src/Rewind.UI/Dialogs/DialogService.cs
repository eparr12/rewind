using Rewind.Core.Sounds;

namespace Rewind.UI.Dialogs;

/// <summary>
/// Shows one Windows 98 dialog at a time over the whole register (UI-5). A screen awaits
/// <see cref="ShowAsync"/>, and the shell's DialogHost draws <see cref="Current"/>. Call it
/// on the UI thread: from a component, or inside InvokeAsync.
/// </summary>
public sealed class DialogService(ISoundPlayer sounds)
{
    private TaskCompletionSource<bool>? _answer;

    /// <summary>The dialog on screen, or null when there is none.</summary>
    public Dialog? Current { get; private set; }

    /// <summary>Raised whenever a dialog opens or closes.</summary>
    public event Action? Changed;

    /// <summary>
    /// Shows a dialog and completes when it closes: true for OK, false for Cancel or Esc.
    /// A new dialog replaces one that's still open, which then counts as cancelled.
    /// Warnings and errors play the error chord, as Windows 98's message boxes did.
    /// </summary>
    public Task<bool> ShowAsync(Dialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        _answer?.TrySetResult(false);
        _answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Current = dialog;
        if (dialog.Icon != DialogIcon.Info)
        {
            sounds.Play(Sound.Error);
        }

        Changed?.Invoke();
        return _answer.Task;
    }

    /// <summary>Closes the dialog on screen with the clerk's answer.</summary>
    public void Close(bool ok)
    {
        var answer = _answer;
        _answer = null;
        Current = null;
        Changed?.Invoke();
        answer?.TrySetResult(ok);
    }
}
