using Rewind.Core.Members;

namespace Rewind.Core.Tests.Members;

public sealed class MemberTests
{
    [Fact]
    public async Task SaveChanges_DuplicateCardBarcode_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.Members.Add(new Member { Name = "Dad", CardBarcode = "MEM-001" });
        await db.SaveChangesAsync(ct);

        db.Members.Add(new Member { Name = "Mom", CardBarcode = "MEM-001" });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("MEM-01")]
    [InlineData("mem-001")]
    [InlineData("RV-00042")]
    public async Task SaveChanges_MalformedCardBarcode_IsRefused(string cardBarcode)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.Members.Add(new Member { Name = "Dad", CardBarcode = cardBarcode });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SaveChanges_BlankName_IsRefused(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.Members.Add(new Member { Name = name, CardBarcode = "MEM-001" });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }
}
