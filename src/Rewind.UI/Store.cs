namespace Rewind.UI;

/// <summary>The store Rewind runs, as its screens name it.</summary>
public static class Store
{
    /// <summary>
    /// The store's default name, from the sign over the counter. It titles Rewind's window and
    /// every dialog, as a Windows 98 program's name titled its message boxes. The name of record
    /// is StoreSettings.StoreName, which the Initial migration fills in with this same text and
    /// the Store Manager edits from Build Plan 3.1 on. From then, screens read the name from there,
    /// and this is only the default.
    /// </summary>
    public const string Name = "Rewind Video";
}
