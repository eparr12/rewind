using System.Globalization;
using Rewind.Core.Shelves;

namespace Rewind.UI.Tests;

/// <summary>
/// The seasonal shelves' Core service as the Shelves tab's tests set it up (ADR-0003). It holds shelves as Core would
/// and saves each change as it was asked for, reading each day as a number, unless the test gives an answer of its
/// own, such as a day that isn't one, which can be a task that hasn't finished. A shelf it saves is in season when its
/// season holds <see cref="Tonight"/>. It records each change it's asked for, with the boxes as typed and the months as
/// picked.
/// </summary>
internal sealed class FakeDisplayShelves : IDisplayShelves
{
    private readonly List<string> _calls = [];
    private readonly List<CancellationToken> _tokens = [];

    /// <summary>The seasonal shelves as Core holds them. A change the fake saves changes them too.</summary>
    public List<SeasonalShelfRow> Rows { get; } = [];

    /// <summary>
    /// The day that decides whether a shelf the fake saves is in season: October 23, so Halloween is and Christmas
    /// isn't.
    /// </summary>
    public DateOnly Tonight { get; set; } = new(2026, 10, 23);

    /// <summary>What a read of the shelves answers instead of <see cref="Rows"/>, such as a read that hasn't finished.</summary>
    public Task<IReadOnlyList<SeasonalShelfRow>>? Listing { get; set; }

    /// <summary>What an add answers instead of saving, such as a name in use.</summary>
    public Task<SeasonalShelfChange>? Adding { get; set; }

    /// <summary>What a rename answers instead of saving, such as a blank name.</summary>
    public Task<SeasonalShelfChange>? Renaming { get; set; }

    /// <summary>What a change of dates answers instead of saving, such as a last day that isn't one.</summary>
    public Task<SeasonalShelfChange>? Redating { get; set; }

    /// <summary>What a deactivation or a reactivation answers instead of saving, such as a save that hasn't finished.</summary>
    public Task<SeasonalShelfChange>? Activating { get; set; }

    /// <summary>
    /// Each change asked for, oldest first, with what was typed in quotes and each month as its number, such as
    /// "Add 'Halloween' 10 '1' 10 '31'".
    /// </summary>
    public IReadOnlyList<string> Calls => _calls;

    /// <summary>The token each call was given, the read's included, oldest first.</summary>
    public IReadOnlyList<CancellationToken> Tokens => _tokens;

    /// <summary>The token the last call was given.</summary>
    public CancellationToken LastToken => _tokens[^1];

    public Task<IReadOnlyList<SeasonalShelfRow>> ListSeasonalAsync(CancellationToken cancellationToken)
    {
        _tokens.Add(cancellationToken);
        return Listing ?? Task.FromResult<IReadOnlyList<SeasonalShelfRow>>([.. Rows]);
    }

    public Task<SeasonalShelfChange> AddAsync(
        string name, int fromMonth, string fromDay, int toMonth, string toDay, CancellationToken cancellationToken)
    {
        _tokens.Add(cancellationToken);
        _calls.Add($"Add '{name}' {fromMonth} '{fromDay}' {toMonth} '{toDay}'");
        return Adding ?? Saved(Row(NextId(), name, SeasonOf(fromMonth, fromDay, toMonth, toDay), isActive: true));
    }

    public Task<SeasonalShelfChange> RenameAsync(int shelfId, string name, CancellationToken cancellationToken)
    {
        _tokens.Add(cancellationToken);
        _calls.Add($"Rename {shelfId} '{name}'");
        return Renaming ?? Saved(Find(shelfId) with { Name = name });
    }

    public Task<SeasonalShelfChange> SetSeasonAsync(
        int shelfId, int fromMonth, string fromDay, int toMonth, string toDay, CancellationToken cancellationToken)
    {
        _tokens.Add(cancellationToken);
        _calls.Add($"SetSeason {shelfId} {fromMonth} '{fromDay}' {toMonth} '{toDay}'");
        return Redating ?? Saved(Row(shelfId, Find(shelfId).Name, SeasonOf(fromMonth, fromDay, toMonth, toDay), Find(shelfId).IsActive));
    }

    public Task<SeasonalShelfChange> SetActiveAsync(int shelfId, bool isActive, CancellationToken cancellationToken)
    {
        _tokens.Add(cancellationToken);
        _calls.Add($"SetActive {shelfId} {isActive}");
        return Activating ?? Saved(Find(shelfId) with { IsActive = isActive });
    }

    // Each day as a number, for the shelf the fake saves. Core reads a typed day more strictly, with a reader the tests
    // can't reach, so a test of a day that isn't one gives Core's answer itself.
    private static Season SeasonOf(int fromMonth, string fromDay, int toMonth, string toDay) =>
        new(fromMonth, int.Parse(fromDay, CultureInfo.InvariantCulture), toMonth, int.Parse(toDay, CultureInfo.InvariantCulture));

    private SeasonalShelfRow Row(int shelfId, string name, Season season, bool isActive) =>
        new(shelfId, name, season, isActive, season.Holds(Tonight));

    private SeasonalShelfRow Find(int shelfId) => Rows.Single(row => row.Id == shelfId);

    private int NextId() => Rows.Count == 0 ? 1 : Rows.Max(row => row.Id) + 1;

    private Task<SeasonalShelfChange> Saved(SeasonalShelfRow shelf)
    {
        Rows.RemoveAll(row => row.Id == shelf.Id);
        Rows.Add(shelf);
        return Task.FromResult(new SeasonalShelfChange(SeasonalShelfChangeStatus.Saved, shelf));
    }
}
