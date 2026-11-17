namespace Rewind.UI.Dialogs;

/// <summary>One line of a dialog's checklist: its words, beside a box that's ticked or not.</summary>
/// <param name="Words">What the line says.</param>
/// <param name="Ticked">Whether its box is ticked.</param>
public sealed record ChecklistLine(string Words, bool Ticked);
