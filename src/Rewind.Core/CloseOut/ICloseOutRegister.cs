namespace Rewind.Core.CloseOut;

/// <summary>
/// What the Close Out Register button asks of Core: the whole Close Out, in one call (ARCH-5), which answers with the
/// checklist's report. <see cref="CloseOutRegister"/> does it.
/// </summary>
public interface ICloseOutRegister
{
    /// <summary>
    /// Closes out the register (Build Plan 3.5, ADR-0013): it closes the open night, backs up the database, writes the
    /// phone catalog to the outbox, copies the backups to the backup drive and keeps the register's newest backups, in
    /// that order, and answers with what the first four came to. With no night open, as on a night of returns only or
    /// at a second Close Out the same night, closing answers that none was open, and the rest still runs. Only closing
    /// the night can fail the call: when that throws, so does this, and nothing else has happened. Each step after it
    /// is tried whatever the one before came to, and one that throws is logged as a warning and reported as not done
    /// (GEN-7). The token can stop the call only until the night is closed, which includes while it waits for another
    /// Close Out: a cancel then throws, and nothing has happened. From then on nothing hears it, so the report comes
    /// back even if the token is cancelled meanwhile (NET-7). Two calls never overlap: a second waits for the first to
    /// finish, then runs.
    /// </summary>
    Task<CloseOutReport> RunAsync(CancellationToken cancellationToken);
}
