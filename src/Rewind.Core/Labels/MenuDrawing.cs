namespace Rewind.Core.Labels;

/// <summary>What drawing the concession menu came to: the menu, or why there's none.</summary>
/// <param name="Status">Whether the menu was drawn.</param>
/// <param name="Menu">The menu, ready to print. Null unless the status is <see cref="MenuStatus.Drawn"/>.</param>
public sealed record MenuDrawing(MenuStatus Status, ConcessionMenu? Menu);
