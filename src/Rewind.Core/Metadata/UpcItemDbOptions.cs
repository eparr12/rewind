namespace Rewind.Core.Metadata;

/// <summary>
/// How Rewind reaches UPCitemdb (NET-11). The free plan needs no signup and no key, so the only
/// setting is where it answers.
/// </summary>
public sealed class UpcItemDbOptions
{
    /// <summary>The settings section these options are read from.</summary>
    public const string SectionName = "UpcItemDb";

    /// <summary>
    /// Where UPCitemdb's free plan answers, ending in a slash. Tests point it at a fake one. An
    /// address that answers 404 Not Found for everything would have every barcode saved as not
    /// found, so the register keeps this one.
    /// </summary>
    public Uri BaseUrl { get; set; } = new("https://api.upcitemdb.com/prod/trial/");

    /// <summary>
    /// Whether lookups can go to an address: a full https address that ends in a slash, with no
    /// query, such as https://api.upcitemdb.com/prod/trial/. Without the slash, a lookup would
    /// replace the address's last part instead of going under it.
    /// </summary>
    public static bool IsValidBaseUrl(Uri? baseUrl) =>
        baseUrl is not null
        && baseUrl.IsAbsoluteUri
        && baseUrl.Scheme == Uri.UriSchemeHttps
        && baseUrl.AbsolutePath.EndsWith('/')
        && baseUrl.Query.Length == 0
        && baseUrl.Fragment.Length == 0;
}
