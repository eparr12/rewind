namespace Rewind.Register;

/// <summary>
/// How large the register's monitor draws Rewind (UI-8, NET-11). Rewind is laid out for
/// 1024 × 768 CSS pixels, and a bigger monitor zooms it by a whole number, which keeps the
/// pixel fonts sharp. This is the clerk's monitor, not the pole display.
/// </summary>
internal sealed class DisplayOptions
{
    /// <summary>The settings section these options are read from.</summary>
    public const string SectionName = "Display";

    /// <summary>The zoom that draws Rewind at its normal size, and the smallest allowed.</summary>
    public const int NormalZoom = 1;

    /// <summary>How many times WebView2 enlarges Rewind, such as 2 to double everything.</summary>
    public int Zoom { get; set; } = NormalZoom;
}
