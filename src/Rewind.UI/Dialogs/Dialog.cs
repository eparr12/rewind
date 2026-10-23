namespace Rewind.UI.Dialogs;

/// <summary>A Windows 98 message box: a title, a message, and OK with an optional Cancel (UI-5).</summary>
/// <param name="Title">The title bar's text.</param>
/// <param name="Message">The message. A line break in it starts a new line.</param>
/// <param name="Icon">The picture beside the message.</param>
/// <param name="CanCancel">Whether a Cancel button sits beside OK.</param>
public sealed record Dialog(string Title, string Message, DialogIcon Icon = DialogIcon.Info, bool CanCancel = false);
