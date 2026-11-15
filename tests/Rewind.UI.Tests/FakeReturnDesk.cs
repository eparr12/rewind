using Rewind.Core.Returns;

namespace Rewind.UI.Tests;

/// <summary>
/// The return desk's Core service as the Return screen's tests set it up (ADR-0003). It holds the rentals a test
/// gives it and answers each call as Core does: a case is checked in at its first scan, a later scan asks about
/// its rental while that rental's watch log is empty, and a watch log is saved once (ADR-0012). A test can give an
/// answer of its own instead, which can be a task that hasn't finished. It keeps each label and each watch log
/// it's given, as the screen sent them.
/// </summary>
internal sealed class FakeReturnDesk : IReturnDesk
{
    private readonly List<string> _labels = [];
    private readonly List<(int RentalItemId, IReadOnlyList<Watcher> Watchers)> _logs = [];
    private readonly HashSet<string> _back = [];
    private readonly HashSet<int> _logged = [];

    /// <summary>The last rental of the case with each label, out until the label's first scan. Any other label is no case's.</summary>
    public Dictionary<string, ReturnedRental> Rentals { get; } = [];

    /// <summary>What a label answers instead of looking in <see cref="Rentals"/>, such as a retired case.</summary>
    public Task<CheckIn>? CheckingIn { get; set; }

    /// <summary>What logging who watched answers instead of working it out, such as a save that fails.</summary>
    public Task<WatchLogStatus>? Logging { get; set; }

    /// <summary>Each label the screen checked in or asked about, oldest first.</summary>
    public IReadOnlyList<string> Labels => _labels;

    /// <summary>Each watch log the screen sent, with its rental item, oldest first.</summary>
    public IReadOnlyList<(int RentalItemId, IReadOnlyList<Watcher> Watchers)> Logs => _logs;

    /// <summary>The token the last call was given.</summary>
    public CancellationToken LastToken { get; private set; }

    public Task<CheckIn> CheckInAsync(string storeLabel, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        _labels.Add(storeLabel);
        return CheckingIn ?? Task.FromResult(Answer(storeLabel));
    }

    public Task<WatchLogStatus> LogWhoWatchedAsync(int rentalItemId, IReadOnlyList<Watcher> watchers, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        _logs.Add((rentalItemId, watchers));
        return Logging ?? Task.FromResult(Answer(rentalItemId, watchers));
    }

    private CheckIn Answer(string storeLabel)
    {
        if (!Rentals.TryGetValue(storeLabel, out var rental))
        {
            return new CheckIn(CheckInStatus.NoSuchCase, null);
        }

        if (_back.Add(storeLabel))
        {
            return new CheckIn(CheckInStatus.CheckedIn, rental);
        }

        return new CheckIn(CheckInStatus.AlreadyOnShelf, _logged.Contains(rental.RentalItemId) ? null : rental);
    }

    private WatchLogStatus Answer(int rentalItemId, IReadOnlyList<Watcher> watchers)
    {
        if (watchers.Count == 0)
        {
            return WatchLogStatus.NoWatchers;
        }

        return _logged.Add(rentalItemId) ? WatchLogStatus.Logged : WatchLogStatus.AlreadyLogged;
    }
}
