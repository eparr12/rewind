namespace Rewind.Core.Members;

/// <summary>Whether a change to the members was saved, and if not, what to fix (NET-12).</summary>
public enum MemberChangeStatus
{
    /// <summary>The change is saved.</summary>
    Saved,

    /// <summary>The name is blank, and nothing is saved.</summary>
    NoName,

    /// <summary>The barcode isn't a member card's, such as a store label or a factory UPC, and nothing is saved.</summary>
    NotAMemberCard,

    /// <summary>Another member has the card already, active or not, and nothing is saved.</summary>
    CardInUse,
}
