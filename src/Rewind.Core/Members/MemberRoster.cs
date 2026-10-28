using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rewind.Core.Data;
using Rewind.Core.Metadata;
using Rewind.Core.Scans;

namespace Rewind.Core.Members;

/// <summary>
/// Keeps the family's members for the Store Manager (Build Plan 3.1): each with a name and the card
/// printed for them, deactivated when they stop renting, never deleted (DATA-4).
/// </summary>
public sealed partial class MemberRoster(IDbContextFactory<RewindDb> dbFactory, ILogger<MemberRoster> logger) : IMemberRoster
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MemberRow>> ListAsync(CancellationToken cancellationToken)
    {
        // DATA-12: read untracked, straight into rows. NOCASE, so a name typed in lower case sorts
        // among the others.
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Members
            .AsNoTracking()
            .OrderBy(m => EF.Functions.Collate(m.Name, "NOCASE"))
            .ThenBy(m => m.CardBarcode)
            .Select(m => new MemberRow(m.Id, m.Name, m.CardBarcode, m.IsActive))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MemberRow?> FindByCardAsync(string cardBarcode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cardBarcode);

        var card = Barcodes.Classify(cardBarcode);
        if (card.Kind != ScanKind.MemberCard)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await FindByCardAsync(db, card.Code, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MemberChange> AddAsync(string name, string cardBarcode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(cardBarcode);

        if (Clean(name) is not { } cleanName)
        {
            return new MemberChange(MemberChangeStatus.NoName, null);
        }

        var card = Barcodes.Classify(cardBarcode);
        if (card.Kind != ScanKind.MemberCard)
        {
            return new MemberChange(MemberChangeStatus.NotAMemberCard, null);
        }

        try
        {
            return await SaveNewAsync(cleanName, card.Code, cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteExtendedErrorCode: SQLitePCL.raw.SQLITE_CONSTRAINT_UNIQUE })
        {
            // DATA-11: the card was free when SaveNewAsync looked, and another member took it before the
            // save. The unique index is the real guard. Anything else it refused would be a bug.
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await FindByCardAsync(db, card.Code, cancellationToken) is { } owner)
            {
                return CardInUse(owner);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<MemberChange> RenameAsync(int memberId, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (Clean(name) is not { } cleanName)
        {
            return new MemberChange(MemberChangeStatus.NoName, null);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var member = await FindAsync(db, memberId, cancellationToken);
        if (member.Name == cleanName)
        {
            return Saved(member); // OBS-3: the log is a member's only history, so no line for no change
        }

        var oldName = member.Name;
        member.Name = cleanName;
        await db.SaveChangesAsync(cancellationToken);
        LogRenamed(logger, member.CardBarcode, oldName, cleanName);
        return Saved(member);
    }

    /// <inheritdoc />
    public async Task<MemberChange> SetActiveAsync(int memberId, bool isActive, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var member = await FindAsync(db, memberId, cancellationToken);
        if (member.IsActive == isActive)
        {
            return Saved(member); // as for a rename, no line for no change
        }

        member.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        LogActiveChanged(logger, member.CardBarcode, member.Name, isActive ? "active" : "inactive");
        return Saved(member);
    }

    private async Task<MemberChange> SaveNewAsync(string name, string card, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await FindByCardAsync(db, card, cancellationToken) is { } owner)
        {
            return CardInUse(owner);
        }

        var member = new Member { Name = name, CardBarcode = card };
        db.Members.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        LogAdded(logger, card, name);
        return Saved(member);
    }

    private MemberChange CardInUse(MemberRow owner)
    {
        LogCardInUse(logger, owner.CardBarcode, owner.Name);
        return new MemberChange(MemberChangeStatus.CardInUse, owner);
    }

    // Typed by the Store Manager, so it's cleaned as a title typed into Intake is (SEC-5).
    private static string? Clean(string name) => WebAnswers.Clean(name, Member.MaxNameLength);

    // Members are never deleted (DATA-4), so an Id from the Store Manager's list is always there.
    private static async Task<Member> FindAsync(RewindDb db, int memberId, CancellationToken cancellationToken) =>
        await db.Members.SingleOrDefaultAsync(m => m.Id == memberId, cancellationToken)
        ?? throw new InvalidOperationException($"There's no member {memberId}, though members are never deleted.");

    private static Task<MemberRow?> FindByCardAsync(RewindDb db, string card, CancellationToken cancellationToken) =>
        db.Members
            .AsNoTracking()
            .Where(m => m.CardBarcode == card)
            .Select(m => new MemberRow(m.Id, m.Name, m.CardBarcode, m.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    private static MemberChange Saved(Member member) =>
        new(MemberChangeStatus.Saved, new MemberRow(member.Id, member.Name, member.CardBarcode, member.IsActive));

    [LoggerMessage(Level = LogLevel.Information, Message = "Added member {CardBarcode}, {Name}")]
    private static partial void LogAdded(ILogger logger, string cardBarcode, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Renamed member {CardBarcode} from {OldName} to {NewName}")]
    private static partial void LogRenamed(ILogger logger, string cardBarcode, string oldName, string newName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Member {CardBarcode}, {Name}, is {State}")]
    private static partial void LogActiveChanged(ILogger logger, string cardBarcode, string name, string state);

    // Information, not Debug (OBS-3): a card that's someone else's is worth finding in the log later,
    // such as when a sheet of cards was printed twice (ADR-0006).
    [LoggerMessage(Level = LogLevel.Information, Message = "Member card {CardBarcode} is {Name}'s already, so nothing was saved")]
    private static partial void LogCardInUse(ILogger logger, string cardBarcode, string name);
}
