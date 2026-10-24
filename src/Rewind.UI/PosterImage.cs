namespace Rewind.UI;

/// <summary>
/// Shows a poster that Core downloaded or read from disk. The bytes go into the page itself, so the
/// web view never fetches anything from the internet (SEC-6), and only Core decides what's a poster
/// (SEC-5). The same markup would work under another host, such as Blazor Server (ARCH-2).
/// </summary>
internal static class PosterImage
{
    /// <summary>An img element's src for a JPEG poster.</summary>
    public static string Source(byte[] jpeg) => $"data:image/jpeg;base64,{Convert.ToBase64String(jpeg)}";
}
