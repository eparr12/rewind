using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rewind.Core.Catalog;
using Rewind.Core.Data;
using Rewind.Core.LateFees;
using Rewind.Core.Members;
using Rewind.Core.MovieNights;

namespace Rewind.Core.Tests.LateFees;

public sealed class LateFeeDrawsTests
{
    private const string MemberCard = "MEM-001";

    // Enough that a bag free to start with Overdue, as one in four does, can't look like one that isn't.
    private const int ManyMembers = 40;

    // Not a bag this member's own draw would leave, so a test can tell the two apart.
    private const string AnotherScansBag = "C";

    [Fact]
    public async Task DrawAsync_ThreeNights_LeavesOneCardInTheSavedBag()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "", ct);
        var draws = Draws(rewind);

        await draws.DrawAsync(member, ct);
        await NextNightAsync(rewind, ct);
        await draws.DrawAsync(member, ct);
        await NextNightAsync(rewind, ct);
        await draws.DrawAsync(member, ct);

        Assert.Equal(1, (await SavedBagAsync(rewind, member, ct)).Length);
    }

    [Theory]
    [InlineData("CCO", Card.Clear, "CO")]
    [InlineData("OCC", Card.Overdue, "CC")]
    public async Task DrawAsync_BagWithCardsLeft_DrawsTheNextCardAndSavesTheRest(string bag, Card card, string left)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag, ct);

        var draw = await Draws(rewind).DrawAsync(member, ct);

        Assert.Equal(card, draw.Card);
        Assert.Equal(left, await SavedBagAsync(rewind, member, ct));
    }

    // Build Plan 3.2: a rescan can't fish for a Clear, or for another movie.
    [Fact]
    public async Task DrawAsync_RescanSameNight_ReturnsTheFirstDraw()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "OCC", ct);
        await AddMoviesAsync(rewind, 3, ct);
        var draws = Draws(rewind);
        var first = await draws.DrawAsync(member, ct);
        rewind.Time.Advance(TimeSpan.FromMinutes(5));

        var rescan = await draws.DrawAsync(member, ct);

        Assert.Equal(first, rescan);
        Assert.Equal([first], await SavedDrawsAsync(rewind, ct));
    }

    [Fact]
    public async Task DrawAsync_RescanSameNight_LeavesTheBagAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "CCO", ct);
        var draws = Draws(rewind);
        await draws.DrawAsync(member, ct);

        await draws.DrawAsync(member, ct);

        Assert.Equal("CO", await SavedBagAsync(rewind, member, ct));
    }

    [Fact]
    public async Task DrawAsync_NewNight_DrawsAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "CCO", ct);
        var draws = Draws(rewind);
        var lastWeek = await draws.DrawAsync(member, ct);
        await NextNightAsync(rewind, ct);

        var tonight = await draws.DrawAsync(member, ct);

        Assert.NotEqual(lastWeek.BusinessDayId, tonight.BusinessDayId);
        Assert.Equal([lastWeek, tonight], await SavedDrawsAsync(rewind, ct));
        Assert.Equal("O", await SavedBagAsync(rewind, member, ct));
    }

    // The card scan is the night's first scan.
    [Fact]
    public async Task DrawAsync_NoOpenNight_OpensOne()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "", ct);

        var draw = await Draws(rewind).DrawAsync(member, ct);

        var night = await Days(rewind).OpenOrCurrentAsync(ct);
        Assert.Equal(night.Id, draw.BusinessDayId);
        Assert.Equal(Now(rewind), night.OpenedAt);
    }

    [Fact]
    public async Task DrawAsync_OneOpenNight_DrawsOnIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "", ct);
        var night = await Days(rewind).OpenOrCurrentAsync(ct);
        rewind.Time.Advance(TimeSpan.FromHours(1));

        var draw = await Draws(rewind).DrawAsync(member, ct);

        Assert.Equal(night.Id, draw.BusinessDayId);
    }

    [Fact]
    public async Task DrawAsync_FirstScan_SavesTheDrawAtTheClocksTime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "CCO", ct);

        var draw = await Draws(rewind).DrawAsync(member, ct);

        Assert.Equal([draw], await SavedDrawsAsync(rewind, ct));
        Assert.Equal((member, Card.Clear, Now(rewind)), (draw.MemberId, draw.Card, draw.DrawnAt));
    }

    [Fact]
    public async Task DrawAsync_TwoMembersOneNight_EachDrawsFromTheirOwnBag()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var dad = await AddMemberAsync(rewind, "MEM-001", bag: "OCC", ct);
        var mom = await AddMemberAsync(rewind, "MEM-002", bag: "CCO", ct);
        var draws = Draws(rewind);

        var dads = await draws.DrawAsync(dad, ct);
        var moms = await draws.DrawAsync(mom, ct);

        Assert.Equal((dad, Card.Overdue, "CC"), (dads.MemberId, dads.Card, await SavedBagAsync(rewind, dad, ct)));
        Assert.Equal((mom, Card.Clear, "CO"), (moms.MemberId, moms.Card, await SavedBagAsync(rewind, mom, ct)));
    }

    [Fact]
    public async Task DrawAsync_OverdueWithAMovie_CarriesItAsTheFlavorMovie()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "O", ct);
        var movie = Assert.Single(await AddMoviesAsync(rewind, 1, ct));

        var draw = await Draws(rewind).DrawAsync(member, ct);

        Assert.Equal(movie, draw.FlavorMovieId);
        Assert.Equal([draw], await SavedDrawsAsync(rewind, ct));
    }

    [Fact]
    public async Task DrawAsync_OverdueWithNoMovies_HasNoFlavorMovie()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "O", ct);

        var draw = await Draws(rewind).DrawAsync(member, ct);

        Assert.Equal(Card.Overdue, draw.Card);
        Assert.Null(draw.FlavorMovieId);
    }

    [Fact]
    public async Task DrawAsync_ClearWithAMovie_HasNoFlavorMovie()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "C", ct);
        await AddMoviesAsync(rewind, 1, ct);

        var draw = await Draws(rewind).DrawAsync(member, ct);

        Assert.Null(draw.FlavorMovieId);
    }

    [Fact]
    public async Task DrawAsync_SeveralMovies_PicksFromTheWholeCollection()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var movies = await AddMoviesAsync(rewind, 3, ct);
        var members = await AddMembersAsync(rewind, ManyMembers, bag: "O", ct);

        var draws = await DrawForEachAsync(rewind, members, ct);

        Assert.Equal(movies.Select(id => (int?)id), draws.Select(d => d.FlavorMovieId).Distinct().Order());
    }

    // The reshuffle rule reads the member's last card from their draws, so Members needs no second column.
    [Fact]
    public async Task DrawAsync_LastDrawWasOverdueAndBagIsSpent_DoesNotStartWithOverdue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var members = await AddMembersAsync(rewind, ManyMembers, bag: "", ct);
        var lastWeek = await Days(rewind).OpenOrCurrentAsync(ct);
        await AddDrawsAsync(rewind, members, lastWeek.Id, Card.Overdue, Now(rewind), ct);
        await NextNightAsync(rewind, ct);

        var draws = await DrawForEachAsync(rewind, members, ct);

        Assert.DoesNotContain(draws, draw => draw.Card == Card.Overdue);
    }

    // The last draw is the highest Id, the order they happened in. Here the Clear is the newer draw, but its
    // time is earlier, as after a clock set back, so anything reading the Overdue as last gives only Clears.
    [Fact]
    public async Task DrawAsync_NewestDrawByIdWasClear_CanStartWithOverdue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var members = await AddMembersAsync(rewind, ManyMembers, bag: "", ct);
        var twoWeeksAgo = await Days(rewind).OpenOrCurrentAsync(ct);
        await AddDrawsAsync(rewind, members, twoWeeksAgo.Id, Card.Overdue, Now(rewind).AddDays(1), ct);
        await NextNightAsync(rewind, ct);
        var lastWeek = await Days(rewind).OpenOrCurrentAsync(ct);
        await AddDrawsAsync(rewind, members, lastWeek.Id, Card.Clear, Now(rewind).AddDays(-7), ct);
        await NextNightAsync(rewind, ct);

        var draws = await DrawForEachAsync(rewind, members, ct);

        Assert.Contains(draws, draw => draw.Card == Card.Overdue);
    }

    // The last card is the member's own: someone else's Overdue just before doesn't change their bag's odds.
    [Fact]
    public async Task DrawAsync_AnotherMembersLastDrawWasOverdue_CanStartWithOverdue()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var members = await AddMembersAsync(rewind, ManyMembers * 2, bag: "", ct);
        var night = await Days(rewind).OpenOrCurrentAsync(ct);
        var service = Draws(rewind);
        var draws = new List<LateFeeDrawRow>();

        foreach (var pair in members.Chunk(2))
        {
            await AddDrawsAsync(rewind, [pair[0]], night.Id, Card.Overdue, Now(rewind), ct);
            draws.Add(await service.DrawAsync(pair[1], ct));
        }

        Assert.Contains(draws, draw => draw.Card == Card.Overdue);
    }

    // DATA-11: this member has no draw when the service looks, then another scan draws before the save, and
    // the unique index refuses this one.
    [Fact]
    public async Task DrawAsync_DrawnDuringTheSave_ReturnsTheOtherDraw()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "CCO", ct);
        var draws = Draws(rewind, new BeforeEachSave(rewind, cancellationToken => AnotherScanDrawsAsync(rewind, member, cancellationToken)));

        var draw = await draws.DrawAsync(member, ct);

        Assert.Equal(Assert.Single(await SavedDrawsAsync(rewind, ct)), draw);
    }

    // DATA-7: the draw and the bag it left save together, so the refused draw takes its new bag with it.
    [Fact]
    public async Task DrawAsync_DrawnDuringTheSave_KeepsTheOtherDrawsBag()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "CCO", ct);
        var draws = Draws(rewind, new BeforeEachSave(rewind, cancellationToken => AnotherScanDrawsAsync(rewind, member, cancellationToken)));

        await draws.DrawAsync(member, ct);

        Assert.Equal(AnotherScansBag, await SavedBagAsync(rewind, member, ct));
    }

    // Checkout looks the card up first, and members are never deleted (DATA-4), so this is a bug.
    [Fact]
    public async Task DrawAsync_UnknownMember_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var draw = () => Draws(rewind).DrawAsync(42, ct);

        await Assert.ThrowsAsync<InvalidOperationException>(draw);
    }

    // OBS-3: which card a member drew is what they'll ask about later.
    [Fact]
    public async Task DrawAsync_FirstScan_LogsTheCard()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "O", ct);
        var log = new ListLogger<LateFeeDraws>();

        await Draws(rewind, logger: log).DrawAsync(member, ct);

        Assert.Equal([(LogLevel.Information, "Member MEM-001, Dad, drew Overdue on business day 1", (Exception?)null)], log.Entries);
    }

    [Fact]
    public async Task DrawAsync_RescanSameNight_LogsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "CCO", ct);
        await Draws(rewind).DrawAsync(member, ct);
        var log = new ListLogger<LateFeeDraws>();

        await Draws(rewind, logger: log).DrawAsync(member, ct);

        Assert.Empty(log.Entries);
    }

    // NET-12, OBS-3: a rescan is an ordinary slip, found before any save. Left to the unique index, EF Core
    // would log each refused save as an error.
    [Fact]
    public async Task DrawAsync_RescanSameNight_LogsNoError()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "CCO", ct);
        await Draws(rewind).DrawAsync(member, ct);
        var log = new ListLoggerProvider();
        using var logged = new LoggedContexts(rewind, log);

        await Draws(rewind, logged).DrawAsync(member, ct);

        Assert.Contains(log.Entries, entry => entry.Category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)); // the log is EF Core's
        Assert.DoesNotContain(log.Entries, entry => entry.Level >= LogLevel.Error);
    }

    // NET-7: the night hears the token too, so a screen that closes doesn't open a night on its way out.
    [Fact]
    public async Task DrawAsync_Cancelled_OpensNoNight()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "", ct);
        using var leaving = new CancellationTokenSource();
        await leaving.CancelAsync();

        var draw = () => Draws(rewind).DrawAsync(member, leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(draw);
        await using var db = rewind.OpenDb();
        Assert.False(await db.BusinessDays.AnyAsync(ct));
    }

    // NET-7: the save hands its token on too, and nothing is half done.
    [Fact]
    public async Task DrawAsync_CancelledJustBeforeTheSave_SavesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var member = await AddMemberAsync(rewind, MemberCard, bag: "CCO", ct);
        using var leaving = new CancellationTokenSource();
        var draws = Draws(rewind, new BeforeEachSave(rewind, _ => leaving.CancelAsync()));

        var draw = () => draws.DrawAsync(member, leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(draw);
        Assert.Empty(await SavedDrawsAsync(rewind, ct));
        Assert.Equal("CCO", await SavedBagAsync(rewind, member, ct));
    }

    private static LateFeeDraws Draws(
        TempRewind rewind, IDbContextFactory<RewindDb>? dbFactory = null, ILogger<LateFeeDraws>? logger = null) =>
        new(dbFactory ?? rewind.DbFactory, Days(rewind), rewind.Time, rewind.Random, logger ?? NullLogger<LateFeeDraws>.Instance);

    private static BusinessDays Days(TempRewind rewind) => new(rewind.DbFactory, rewind.Time, NullLogger<BusinessDays>.Instance);

    private static DateTime Now(TempRewind rewind) => rewind.Time.GetUtcNow().UtcDateTime;

    // Close Out tonight, so next week's first scan opens another night.
    private static async Task NextNightAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await Days(rewind).CloseAsync(cancellationToken);
        rewind.Time.Advance(TimeSpan.FromDays(7));
    }

    private static async Task<List<LateFeeDrawRow>> DrawForEachAsync(TempRewind rewind, int[] members, CancellationToken cancellationToken)
    {
        var service = Draws(rewind);
        var draws = new List<LateFeeDrawRow>();
        foreach (var member in members)
        {
            draws.Add(await service.DrawAsync(member, cancellationToken));
        }

        return draws;
    }

    private static async Task<int> AddMemberAsync(TempRewind rewind, string card, string bag, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        var member = new Member { Name = "Dad", CardBarcode = card, Bag = bag };
        db.Members.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        return member.Id;
    }

    private static async Task<int[]> AddMembersAsync(TempRewind rewind, int count, string bag, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        var members = Enumerable.Range(1, count)
            .Select(i => new Member { Name = "Dad", CardBarcode = "MEM-" + i.ToString("000", CultureInfo.InvariantCulture), Bag = bag })
            .ToList();
        db.Members.AddRange(members);
        await db.SaveChangesAsync(cancellationToken);
        return [.. members.Select(m => m.Id)];
    }

    private static async Task<int[]> AddMoviesAsync(TempRewind rewind, int count, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        var movies = Enumerable.Range(1, count).Select(i => new Movie { Title = "Movie " + i.ToString(CultureInfo.InvariantCulture) }).ToList();
        db.Movies.AddRange(movies);
        await db.SaveChangesAsync(cancellationToken);
        return [.. movies.Select(m => m.Id)];
    }

    // Earlier draws, saved straight to the database. A Clear or an Overdue with no movie names no flavor.
    private static async Task AddDrawsAsync(
        TempRewind rewind, int[] members, int night, Card card, DateTime drawnAt, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        db.LateFeeDraws.AddRange(members.Select(member => new LateFeeDraw { MemberId = member, BusinessDayId = night, Card = card, DrawnAt = drawnAt }));
        await db.SaveChangesAsync(cancellationToken);
    }

    // What a second scan of the card saves, the moment before this one's save: its own draw, and its own bag.
    private static async Task AnotherScanDrawsAsync(TempRewind rewind, int memberId, CancellationToken cancellationToken)
    {
        var night = await Days(rewind).OpenOrCurrentAsync(cancellationToken);
        await using var db = rewind.OpenDb();
        db.LateFeeDraws.Add(new LateFeeDraw { MemberId = memberId, BusinessDayId = night.Id, Card = Card.Clear, DrawnAt = Now(rewind) });
        (await db.Members.SingleAsync(m => m.Id == memberId, cancellationToken)).Bag = AnotherScansBag;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<string> SavedBagAsync(TempRewind rewind, int memberId, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        return await db.Members.AsNoTracking().Where(m => m.Id == memberId).Select(m => m.Bag).SingleAsync(cancellationToken);
    }

    // The draws as the database holds them, in the order they were saved.
    private static async Task<List<LateFeeDrawRow>> SavedDrawsAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        return await db.LateFeeDraws
            .AsNoTracking()
            .OrderBy(d => d.Id)
            .Select(d => new LateFeeDrawRow(d.Id, d.MemberId, d.BusinessDayId, d.Card, d.FlavorMovieId, d.DrawnAt))
            .ToListAsync(cancellationToken);
    }
}
