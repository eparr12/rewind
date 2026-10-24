namespace Rewind.Core.Intake;

/// <summary>One movie in a case that's just gone on the shelf.</summary>
/// <param name="Title">The title, such as Halloween.</param>
/// <param name="Year">The year it came out. Null when unknown (DATA-3).</param>
public sealed record ShelvedMovie(string Title, int? Year);
