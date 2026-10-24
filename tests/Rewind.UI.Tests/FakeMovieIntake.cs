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
    private readonly List<int> _adds = [];

    /// <summary>What every search answers. TMDB finds nothing until a test says otherwise.</summary>
    public Task<TmdbSearch> Search { get; set; } = Task.FromResult(new TmdbSearch(TmdbStatus.Ok, []));

    /// <summary>
    /// What every add answers. Set it before a pick: the default, TMDB not answering, opens a
    /// dialog, and the pick then waits until the test closes it.
    /// </summary>
    public Task<MovieAdd> Add { get; set; } = Task.FromResult(new MovieAdd(AddStatus.TryLater, null));

    /// <summary>The titles searched for, oldest first.</summary>
    public IReadOnlyList<string> Searches => _searches;

    /// <summary>The TMDB IDs the clerk asked to add, oldest first.</summary>
    public IReadOnlyList<int> Adds => _adds;

    public Task<TmdbSearch> SearchAsync(string title, CancellationToken cancellationToken)
    {
        _searches.Add(title);
        return Search;
    }

    public Task<MovieAdd> AddAsync(int tmdbId, CancellationToken cancellationToken)
    {
        _adds.Add(tmdbId);
        return Add;
    }

    // The Intake screen moves to these in the next pull request (ADR-0005), and this fake answers
    // them then. Until it does, a call means the screen changed without its tests.
    public Task<MoviePick> PickAsync(int tmdbId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The Intake screen doesn't pick through PickAsync yet.");

    public HandEntry EnterByHand(string title, string? year, string? runtimeMinutes, string? rating) =>
        throw new NotSupportedException("The Intake screen doesn't enter movies by hand yet.");

    public Task<Shelving> ShelveAsync(string storeLabel, string? factoryUpc, IReadOnlyList<CaseMovieEntry> movies, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The Intake screen doesn't shelve cases yet.");
}
