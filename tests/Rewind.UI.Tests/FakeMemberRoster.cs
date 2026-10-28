using Rewind.Core.Members;

namespace Rewind.UI.Tests;

/// <summary>
/// The members' Core service as the Members tab's tests set it up (ADR-0003). It holds members as Core
/// would and answers each change as Core does, unless the test gives an answer of its own, which can be
/// a task that hasn't finished. It records each change it's asked for.
/// </summary>
internal sealed class FakeMemberRoster : IMemberRoster
{
    private readonly List<string> _calls = [];

    /// <summary>The members as Core holds them. A change the fake answers as Core would changes them too.</summary>
    public List<MemberRow> Rows { get; } = [];

    /// <summary>What a read of the members answers instead of <see cref="Rows"/>, such as a read that hasn't finished.</summary>
    public Task<IReadOnlyList<MemberRow>>? Listing { get; set; }

    /// <summary>What an add answers instead of saving, such as a card in use.</summary>
    public Task<MemberChange>? Adding { get; set; }

    /// <summary>What a rename answers instead of saving, such as a blank name.</summary>
    public Task<MemberChange>? Renaming { get; set; }

    /// <summary>Each change asked for, oldest first, such as "Add Grandma MEM-001".</summary>
    public IReadOnlyList<string> Calls => _calls;

    /// <summary>The token the last call was given.</summary>
    public CancellationToken LastToken { get; private set; }

    public Task<IReadOnlyList<MemberRow>> ListAsync(CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        return Listing ?? Task.FromResult<IReadOnlyList<MemberRow>>([.. Rows]);
    }

    public Task<MemberChange> AddAsync(string name, string cardBarcode, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        _calls.Add($"Add {name} {cardBarcode}");
        return Adding ?? Saved(new MemberRow(Rows.Count + 1, name, cardBarcode, IsActive: true));
    }

    public Task<MemberChange> RenameAsync(int memberId, string name, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        _calls.Add($"Rename {memberId} {name}");
        return Renaming ?? Saved(Row(memberId) with { Name = name });
    }

    public Task<MemberChange> SetActiveAsync(int memberId, bool isActive, CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        _calls.Add($"SetActive {memberId} {isActive}");
        return Saved(Row(memberId) with { IsActive = isActive });
    }

    private MemberRow Row(int memberId) => Rows.Single(row => row.Id == memberId);

    private Task<MemberChange> Saved(MemberRow member)
    {
        Rows.RemoveAll(row => row.Id == member.Id);
        Rows.Add(member);
        return Task.FromResult(new MemberChange(MemberChangeStatus.Saved, member));
    }
}
