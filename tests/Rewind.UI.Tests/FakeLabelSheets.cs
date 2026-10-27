using System.Globalization;
using Rewind.Core.Labels;

namespace Rewind.UI.Tests;

/// <summary>
/// The label sheets' Core service as the Store Manager's tests set it up (ADR-0003): it answers with
/// whatever the test gives it, which can be a task that hasn't finished, and counts the sheets it was
/// asked to make. Its sheets carry real barcodes, drawn by Code128.
/// </summary>
internal sealed class FakeLabelSheets : ILabelSheets
{
    /// <summary>A sheet of store labels from RV-00031 to RV-00060, as the second sheet would be.</summary>
    public static readonly Sheet StoreLabels = NewSheet(
        SheetKind.StoreLabels, "RV-{0:D5}", 31, LabelSheets.LabelsPerSheet, Code128.StoreLabelWidthInches, Code128.StoreLabelHeightInches);

    /// <summary>A sheet of member cards from MEM-001 to MEM-010, as the first sheet would be.</summary>
    public static readonly Sheet MemberCards = NewSheet(
        SheetKind.MemberCards, "MEM-{0:D3}", 1, LabelSheets.CardsPerSheet, Code128.MemberCardWidthInches, Code128.MemberCardHeightInches);

    /// <summary>A sheet that would pass the last number its codes can carry.</summary>
    public static readonly SheetReservation OutOfNumbers = new(SheetStatus.OutOfNumbers, null);

    /// <summary>What every read of the next codes answers.</summary>
    public Task<NextCodes> Next { get; set; } = Task.FromResult(new NextCodes("RV-00031", "MEM-001"));

    /// <summary>What every sheet of store labels comes to: <see cref="StoreLabels"/> until a test says otherwise.</summary>
    public Task<SheetReservation> Labels { get; set; } = Task.FromResult(new SheetReservation(SheetStatus.Reserved, StoreLabels));

    /// <summary>What every sheet of member cards comes to: <see cref="MemberCards"/> until a test says otherwise.</summary>
    public Task<SheetReservation> Cards { get; set; } = Task.FromResult(new SheetReservation(SheetStatus.Reserved, MemberCards));

    /// <summary>How many sheets of store labels the screen asked for.</summary>
    public int LabelSheetsMade { get; private set; }

    /// <summary>How many sheets of member cards the screen asked for.</summary>
    public int CardSheetsMade { get; private set; }

    public Task<NextCodes> PeekAsync(CancellationToken cancellationToken) => Next;

    public Task<SheetReservation> ReserveLabelsAsync(CancellationToken cancellationToken)
    {
        LabelSheetsMade++;
        return Labels;
    }

    public Task<SheetReservation> ReserveCardsAsync(CancellationToken cancellationToken)
    {
        CardSheetsMade++;
        return Cards;
    }

    private static Sheet NewSheet(SheetKind kind, string format, int first, int count, double widthInches, double heightInches) =>
        new(
            kind,
            "Rewind Video",
            [.. Enumerable.Range(first, count).Select(number =>
            {
                var code = string.Format(CultureInfo.InvariantCulture, format, number);
                return new SheetBarcode(code, Code128.Svg(code, widthInches, heightInches));
            })]);
}
