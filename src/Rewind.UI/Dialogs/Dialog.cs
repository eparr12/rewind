namespace Rewind.UI.Dialogs;

/// <summary>
/// A Windows 98 message box: a title, a message, an optional checklist, and OK with an optional Cancel (UI-5).
/// </summary>
/// <param name="Title">The title bar's text.</param>
/// <param name="Message">The message. A line break in it starts a new line.</param>
/// <param name="Icon">The picture beside the message.</param>
/// <param name="CanCancel">Whether a Cancel button sits beside OK.</param>
public sealed record Dialog(string Title, string Message, DialogIcon Icon = DialogIcon.Info, bool CanCancel = false)
{
    /// <summary>
    /// The checklist's lines, in order, drawn between the message and the buttons, such as what Close Out did.
    /// Each box shows whether its line is ticked, and the clerk can't change it. Empty, the default, for a dialog
    /// with no checklist.
    /// </summary>
    public IReadOnlyList<ChecklistLine> Checklist { get; init; } = [];
}
