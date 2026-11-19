using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rewind.Core.Tests;

/// <summary>
/// Runs something just after each explicit transaction commits, for a test of what a service does
/// once its write is in, such as a clerk who leaves the screen just then. A cancel from here on must not turn the
/// done outcome into an exception (NET-7).
/// </summary>
internal sealed class AfterEachCommit(Func<CancellationToken, Task> after) : DbTransactionInterceptor
{
    public override async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default) =>
        await after(cancellationToken);
}
