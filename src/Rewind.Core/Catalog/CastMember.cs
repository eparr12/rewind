namespace Rewind.Core.Catalog;

/// <summary>
/// One of a movie's top-billed actors, from TMDB. A refresh replaces a movie's cast (DATA-9).
/// </summary>
public sealed class CastMember
{
    /// <summary>The database key.</summary>
    public int Id { get; set; }

    /// <summary>The movie the actor is in.</summary>
    public int MovieId { get; set; }

    /// <summary>The actor's name, such as Jamie Lee Curtis.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The actor's place in the billing: 0 is the top of the bill. No two of a movie's actors
    /// share one.
    /// </summary>
    public int BillingOrder { get; set; }
}
