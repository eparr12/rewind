using System.Diagnostics;
using System.Globalization;
using Rewind.Core.CloseOut;
using Rewind.Core.MovieNights;
using Rewind.UI.Dialogs;

namespace Rewind.UI.Screens;

/// <summary>
/// The checklist a Close Out ends with (Build Plan 3.5), made from what Core reported: one line for each step, in
/// order, ticked when it worked. A line that isn't ticked says what to do, in the clerk's words. When everything
/// worked, it's Info, which plays nothing. Otherwise it's a Warning, which plays the error chord, and never an Error:
/// nothing crashed, and a Close Out always finishes (ADR-0013).
/// </summary>
public static class CloseOutChecklist
{
    private const string EverythingWorked = "Close Out is done, and everything worked.";

    private const string NotEverythingWorked =
        "Close Out is done, but not everything worked. Each line without a tick says what to do.";

    private const string TellTheStoreManager = "Tell the Store Manager.";

    /// <summary>The checklist dialog for what a Close Out came to.</summary>
    public static Dialog For(CloseOutReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var (message, icon) = report.EverythingWorked
            ? (EverythingWorked, DialogIcon.Info)
            : (NotEverythingWorked, DialogIcon.Warning);
        return new(Store.Name, message, icon)
        {
            Checklist = [Night(report.Night), Backup(report.Backup), Catalog(report.CatalogMovies), Copy(report.Copy)],
        };
    }

    // ADR-0013: a night of returns only, or a second Close Out the same night, has no night open. That's no problem,
    // so its box is ticked too.
    private static ChecklistLine Night(BusinessDayClosingStatus night) => night switch
    {
        BusinessDayClosingStatus.Closed => new("The movie night is closed.", Ticked: true),
        BusinessDayClosingStatus.NothingOpen => new("No movie night was open, so there was nothing to close.", Ticked: true),
        _ => throw new UnreachableException($"Closing the night came back {night}."),
    };

    private static ChecklistLine Backup(string? backup) => backup is null
        ? new($"The backup wasn't saved. {TellTheStoreManager}", Ticked: false)
        : new($"The backup {backup} is saved.", Ticked: true);

    private static ChecklistLine Catalog(int? movies) => movies is { } count
        ? new($"The phone catalog is written, with {Count(count, "movie")}.", Ticked: true)
        : new($"The phone catalog wasn't written. {TellTheStoreManager}", Ticked: false);

    // No copy is one that stopped on a failure that wasn't a drive's. The newest backup goes first, so tonight's may be
    // on the drive already, and the line doesn't say that nothing was copied.
    private static ChecklistLine Copy(DriveCopy? copy) => copy is null
        ? new($"Copying to the backup drive stopped partway. {TellTheStoreManager}", Ticked: false)
        : copy.Status switch
        {
            DriveCopyStatus.Copied => new($"Copied {Count(copy.Files, "backup")}{InAll(copy.Drives)} to the {Drives(copy.Drives)}.", Ticked: true),
            DriveCopyStatus.NothingToCopy => new($"The {Drives(copy.Drives)} already had every backup.", Ticked: true),
            DriveCopyStatus.NoDrive => new("No backup drive is plugged in. Plug the backup drive in, then press Close Out Register again.", Ticked: false),
            DriveCopyStatus.Unwritable => new($"Rewind couldn't copy to the {Drives(copy.Unwritable)}. {TellTheStoreManager}", Ticked: false),
            _ => throw new UnreachableException($"The copy to the backup drive came back {copy.Status}."),
        };

    // Files counts each copy, so one backup copied to two drives is two: with more than one drive, it's a total.
    private static string InAll(IReadOnlyList<string> drives) => drives.Count > 1 ? " in all" : "";

    // Such as "backup drive E:\", or "backup drives E:\, F:\ and G:\".
    private static string Drives(IReadOnlyList<string> drives) => drives switch
    {
        [var drive] => $"backup drive {drive}",
        [.., var last] => $"backup drives {string.Join(", ", drives.Take(drives.Count - 1))} and {last}",
        [] => throw new UnreachableException("The copy to the backup drive named no drive."),
    };

    // Such as "1 movie" or "1,234 movies", the same on any PC.
    private static string Count(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count:N0} {noun}{(count == 1 ? "" : "s")}");
}
