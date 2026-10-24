namespace Rewind.Core.Metadata;

/// <summary>
/// How Rewind reaches TMDB (NET-11). The token is a secret, so it's never in the repo or in
/// appsettings.json (SEC-1): a development PC keeps it in dotnet user-secrets, and the register in
/// keys\secrets.json. The README shows both.
/// </summary>
public sealed class TmdbOptions
{
    /// <summary>The settings section these options are read from.</summary>
    public const string SectionName = "Tmdb";

    /// <summary>
    /// TMDB's API Read Access Token, from the API page of TMDB's account settings: the long one,
    /// not the 32-character API Key. Empty until the Store Manager sets it up. Rewind still starts
    /// without it, since only intake needs TMDB (GEN-7), and intake then says it can't reach TMDB.
    /// </summary>
    public string AccessToken { get; set; } = "";

    /// <summary>
    /// Whether a token can go in an HTTP header as it is: printable ASCII with no spaces, such as
    /// "Bearer " pasted in front of it or a line break after it. An empty token is allowed, since
    /// it means TMDB isn't set up yet. Whether TMDB accepts the token, only TMDB can say.
    /// </summary>
    public static bool IsWellFormed(string? token) =>
        token is not null && token.All(c => c is > ' ' and <= '~');
}
