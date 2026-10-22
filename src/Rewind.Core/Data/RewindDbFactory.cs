using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Rewind.Core.Data;

/// <summary>
/// Lets dotnet ef create <see cref="RewindDb"/> without starting the register. It points at a
/// scratch file, design.db, so the tools never touch the real database: Rewind migrates that
/// itself, after a backup (DATA-5).
/// </summary>
public sealed class RewindDbFactory : IDesignTimeDbContextFactory<RewindDb>
{
    private const string ScratchDatabase = "design.db";

    /// <inheritdoc />
    public RewindDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<RewindDb>()
            .UseSqlite(RewindDb.ConnectionStringFor(ScratchDatabase))
            .Options);
}
