using Microsoft.EntityFrameworkCore;
using Rewind.Core.Data;

namespace Rewind.Core.Tests;

/// <summary>
/// Contexts that the service under test must never ask for, for a test of what it decides before it needs the
/// database, such as a scan that isn't a store label, or a null it refuses. Asking throws, and the message names
/// the service that asked.
/// </summary>
internal sealed class NoDatabase(string service) : IDbContextFactory<RewindDb>
{
    public RewindDb CreateDbContext() => throw new InvalidOperationException($"{service} opened the database.");
}
