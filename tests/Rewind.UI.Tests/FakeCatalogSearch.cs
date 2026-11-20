using Rewind.Core.Catalog;

namespace Rewind.UI.Tests;

/// <summary>
/// The catalog search as Ask the Clerk's tests set it up (ADR-0003): it answers with whatever the test gives it, which
/// can be a task that hasn't finished, and keeps each search it's asked for, with the four boxes as the screen sent
/// them.
/// </summary>
internal sealed class FakeCatalogSearch : ICatalogSearch
{
    private readonly List<(string? Text, string? Genre, string? Year, string? Rating)> _searches = [];

    /// <summary>What every read of the genres answers. No genres until a test says otherwise.</summary>
    public Task<IReadOnlyList<string>> Genres { get; set; } = Task.FromResult<IReadOnlyList<string>>([]);

    /// <summary>What every search answers. Nothing found until a test says otherwise.</summary>
    public Task<SearchResult> Search { get; set; } = Task.FromResult(new SearchResult(SearchStatus.Searched, [], MoreMatched: false));

    /// <summary>How many times the genres were read.</summary>
    public int GenreReads { get; private set; }

    /// <summary>Each search the screen asked for, oldest first.</summary>
    public IReadOnlyList<(string? Text, string? Genre, string? Year, string? Rating)> Searches => _searches;

    /// <summary>The token the last call was given.</summary>
    public CancellationToken LastToken { get; private set; }

    public Task<IReadOnlyList<string>> GenresAsync(CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        GenreReads++;
        return Genres;
    }

    public Task<SearchResult> SearchAsync(string? text, string? genre, string? year, string? rating, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        _searches.Add((text, genre, year, rating));
        return Search;
    }
}
