namespace Rewind.Core.Labels;

/// <summary>How drawing the concession menu went (NET-12).</summary>
public enum MenuStatus
{
    /// <summary>The menu is drawn, and ready to print.</summary>
    Drawn,

    /// <summary>No snack is on sale, so there's nothing to put on a menu.</summary>
    NothingOnSale,
}
