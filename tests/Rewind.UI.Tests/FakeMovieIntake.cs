using Rewind.Core.Intake;
using Rewind.Core.Metadata;

namespace Rewind.UI.Tests;

/// <summary>
/// Intake's Core service as the screen tests set it up (ADR-0003): it answers with whatever the test
/// gives it, which can be a task that hasn't finished, and records what it was asked.
/// </summary>
internal sealed class FakeMovieIntake : IMovieIntake
{
    private readonly List<string> _searches = [];
    private readonly List<int> _picks = [];
    private readonly List<(string Title, string? Year, string? RuntimeMinutes, string? Rating)> _handEntries = [];
    private readonly List<(string StoreLabel, string? FactoryUpc, IReadOnlyList<CaseMovieEntry> Movies)> _shelvings = [];

    // How many cases the fake's saves have put on the shelf, which each count adds to OnShelf.
    private int _shelved;

    /// <summary>What every search answers. TMDB finds nothing until a test says otherwise.</summary>
    public Task<TmdbSearch> Search { get; set; } = Task.FromResult(new TmdbSearch(TmdbStatus.Ok, []));

    /// <summary>
    /// What every pick answers. Set it before a pick: the default, TMDB not answering, opens a
    /// dialog, and the pick then waits until the test closes it.
    /// </summary>
    public Task<MoviePick> Pick { get; set; } = Task.FromResult(new MoviePick(PickStatus.TryLater, null));

    /// <summary>
    /// What every movie typed in by hand comes to. Until a test says otherwise, Core finds the title
    /// blank, which the screen says in its prompt line.
    /// </summary>
    public HandEntry HandEntry { get; set; } = new(HandEntryStatus.NoTitle, null);

    /// <summary>
    /// What every label scan's save answers. Set it before a label scan: the default, a label in
    /// use, opens a dialog, which then waits until the test closes it.
    /// </summary>
    public Task<Shelving> Shelving { get; set; } = Task.FromResult(new Shelving(ShelveStatus.LabelInUse, null));

    /// <summary>
    /// How many cases were on the shelf before the test shelved any: none, until a test says
    /// otherwise. A count answers this plus one for each case shelved since, as Core's would.
    /// </summary>
    public Task<int> OnShelf { get; set; } = Task.FromResult(0);

    /// <summary>The token the last count was given.</summary>
    public CancellationToken LastCountToken { get; private set; }

    /// <summary>The titles searched for, oldest first.</summary>
    public IReadOnlyList<string> Searches => _searches;

    /// <summary>The TMDB IDs the clerk picked, oldest first.</summary>
    public IReadOnlyList<int> Picks => _picks;

    /// <summary>What the clerk typed for each movie entered by hand, oldest first.</summary>
    public IReadOnlyList<(string Title, string? Year, string? RuntimeMinutes, string? Rating)> HandEntries => _handEntries;

    /// <summary>Each case the clerk asked to shelve: its label, its barcode and its movies, oldest first.</summary>
    public IReadOnlyList<(string StoreLabel, string? FactoryUpc, IReadOnlyList<CaseMovieEntry> Movies)> Shelvings => _shelvings;

    public Task<TmdbSearch> SearchAsync(string title, CancellationToken cancellationToken)
    {
        _searches.Add(title);
        return Search;
    }

    public Task<MoviePick> PickAsync(int tmdbId, CancellationToken cancellationToken)
    {
        _picks.Add(tmdbId);
        return Pick;
    }

    public HandEntry EnterByHand(string title, string? year, string? runtimeMinutes, string? rating)
    {
        _handEntries.Add((title, year, runtimeMinutes, rating));
        return HandEntry;
    }

    public async Task<Shelving> ShelveAsync(string storeLabel, string? factoryUpc, IReadOnlyList<CaseMovieEntry> movies, CancellationToken cancellationToken)
    {
        _shelvings.Add((storeLabel, factoryUpc, movies));
        var shelving = await Shelving;
        if (shelving.Status == ShelveStatus.Shelved)
        {
            _shelved++;
        }

        return shelving;
    }

    public async Task<int> CountOnShelfAsync(CancellationToken cancellationToken)
    {
        LastCountToken = cancellationToken;
        return await OnShelf + _shelved;
    }
}
