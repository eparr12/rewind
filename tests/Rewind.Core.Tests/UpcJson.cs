using System.Text.Json;

namespace Rewind.Core.Tests;

/// <summary>
/// UPCitemdb's answers, written out by hand in its own shape, so a test fails if the client reads a
/// name UPCitemdb doesn't send. <see cref="TommyBoy"/> is its real answer for 032429258373 on
/// October 24, 2026, cut down to some of its fields, with the description made up. The error
/// answers follow UPCitemdb's documentation.
/// </summary>
internal static class UpcJson
{
    /// <summary>Tommy Boy on DVD, as a 12-digit UPC-A.</summary>
    public const string TommyBoyUpc = "032429258373";

    /// <summary>The same barcode as a 13-digit EAN, which starts with 0.</summary>
    public const string TommyBoyEan = "0032429258373";

    /// <summary>Another barcode with a valid check digit, for a second DVD.</summary>
    public const string HalloweenUpc = "036000291452";

    public const string TommyBoy = """
        {
          "code": "OK",
          "total": 1,
          "offset": 0,
          "items": [
            {
              "ean": "0032429258373",
              "title": "Tommy Boy (DVD)",
              "description": "A party animal hits the road to save the family business.",
              "upc": "032429258373",
              "brand": "Paramount Home Video",
              "model": "31876643",
              "color": "",
              "size": "",
              "dimension": "",
              "weight": "0.00 lb",
              "category": "Electronics > Video > Televisions",
              "currency": "",
              "lowest_recorded_price": 1.73,
              "highest_recorded_price": 60,
              "images": [],
              "offers": []
            }
          ]
        }
        """;

    /// <summary>A barcode UPCitemdb has no product for, answered 200 OK with no items.</summary>
    public const string NoItems = """{"code": "OK", "total": 0, "offset": 0, "items": []}""";

    /// <summary>UPCitemdb's burst limit: about 6 lookups a minute.</summary>
    public static readonly string TooFast = Error("TOO_FAST", "Request exceeds burst rate limit.");

    /// <summary>UPCitemdb's daily limit: 100 lookups a day.</summary>
    public static readonly string ExceedLimit = Error("EXCEED_LIMIT", "Exceed daily request limits.");

    /// <summary>A lookup's answer with one item for each title, first title first.</summary>
    public static string Items(params string?[] titles) =>
        $$"""
        {
          "code": "OK",
          "total": {{titles.Length}},
          "offset": 0,
          "items": [{{string.Join(", ", titles.Select(title => $$"""{"ean": "0036000291452", "title": {{Text(title)}}, "upc": "036000291452"}"""))}}]
        }
        """;

    /// <summary>An error answer, such as TOO_FAST.</summary>
    public static string Error(string code, string message) => $$"""{"code": {{Text(code)}}, "message": {{Text(message)}}}""";

    // A JSON string, quoted and escaped, or null.
    private static string Text(string? value) => JsonSerializer.Serialize(value);
}
