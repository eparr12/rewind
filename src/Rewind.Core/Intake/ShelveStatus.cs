namespace Rewind.Core.Intake;

/// <summary>How saving a case at its label scan went (NET-12).</summary>
public enum ShelveStatus
{
    /// <summary>The case is on the shelf, saved with its movies, and the new movies' posters are saved.</summary>
    Shelved,

    /// <summary>Another case has the label already, so nothing was saved. The clerk peels the next label.</summary>
    LabelInUse,

    /// <summary>The case has no movies, so nothing was saved.</summary>
    NoMovies,

    /// <summary>The label isn't a store label, such as RV-00042, so nothing was saved.</summary>
    NotALabel,

    /// <summary>The case's factory barcode isn't a UPC or an EAN, so nothing was saved.</summary>
    NotAUpc,
}
