namespace Rewind.Core.Metadata;

/// <summary>The sizes Rewind downloads TMDB's posters at, named for where each one is shown.</summary>
public enum PosterSize
{
    /// <summary>92 pixels wide, for the picker's list. Never saved.</summary>
    Picker,

    /// <summary>342 pixels wide, for the register's screens.</summary>
    Register,

    /// <summary>154 pixels wide, for the phone catalog's thumbnails.</summary>
    Thumb,
}
