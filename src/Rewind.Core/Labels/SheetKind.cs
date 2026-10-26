namespace Rewind.Core.Labels;

/// <summary>What a printed sheet holds, which decides its layout and its barcodes' size.</summary>
public enum SheetKind
{
    /// <summary>Removable store labels for the cases, such as RV-00042.</summary>
    StoreLabels,

    /// <summary>Membership cards on card stock, such as MEM-001, to cut out and laminate.</summary>
    MemberCards,
}
