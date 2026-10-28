using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rewind.Core.Data;

namespace Rewind.Core.Tests;

/// <summary>
/// Contexts that write EF Core's own log to a list, at every level, for a test of what EF Core logs,
/// such as no error for a refusal a service finds before its save. One logger factory serves them all,
/// as one serves the register, so EF Core builds its services once.
/// </summary>
internal sealed class LoggedContexts(TempRewind rewind, ListLoggerProvider log) : IDbContextFactory<RewindDb>, IDisposable
{
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(log));

    public RewindDb CreateDbContext() =>
        new(new DbContextOptionsBuilder<RewindDb>()
            .UseSqlite(RewindDb.ConnectionStringFor(rewind.Paths.Database))
            .UseLoggerFactory(_loggerFactory)
            .Options);

    public void Dispose() => _loggerFactory.Dispose();
}
