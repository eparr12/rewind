using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rewind.Core.Tests;

/// <summary>
/// Runs something just before each save, for a test of what a service does when something
/// gets in first, such as another save that a unique index then holds against this one. A trigger can't
/// stand in for that other save: SQLite rolls a trigger's insert back along with the insert that fails, so
/// the row would be gone when the service looks after the refusal.
/// </summary>
internal sealed class BeforeEachSave(Func<CancellationToken, Task> before) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await before(cancellationToken);
        return result;
    }
}
