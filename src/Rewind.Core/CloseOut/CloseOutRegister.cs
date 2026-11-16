using Microsoft.Extensions.Logging;
using Rewind.Core.Data;
using Rewind.Core.MovieNights;

namespace Rewind.Core.CloseOut;

/// <summary>
/// Closes out the register (Build Plan 3.5). Only closing the night can fail a Close Out, and each step after it is
/// tried and reported, whatever the one before came to (ADR-0013). The steps are the business days', the backups', the
/// phone catalog's and the backup drive's own calls, so this holds only their order, the guard around each one, and
/// the rule that one Close Out runs at a time. It's a singleton, so that rule covers the whole register.
/// </summary>
public sealed partial class CloseOutRegister(
    IBusinessDays days,
    Backups backups,
    PhoneCatalog catalog,
    BackupDrive drive,
    ILogger<CloseOutRegister> logger) : ICloseOutRegister, IDisposable
{
    // What the Close Out's log line says for a step that wasn't done, and for a catalog that was (GEN-4).
    private const string NotSaved = "not saved";
    private const string Written = "written";
    private const string NotWritten = "not written";
    private const string Failed = "failed";

    // Two Close Outs at once would both find the night open, and race for one backup's name.
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <inheritdoc />
    public async Task<CloseOutReport> RunAsync(CancellationToken cancellationToken)
    {
        await _oneAtATime.WaitAsync(cancellationToken);
        try
        {
            var closing = await days.CloseAsync(cancellationToken);
            return await RunTheRestAsync(closing.Status);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _oneAtATime.Dispose();

    // NET-7: the night is closed, so from here on nothing hears the token, and the report always comes back. Each step
    // is tried whatever the one before came to (GEN-7). The backup comes after closing, so it holds the closed night,
    // and the oldest backups are deleted after the copy, so one on its way out still reaches the drive first.
    private async Task<CloseOutReport> RunTheRestAsync(BusinessDayClosingStatus night)
    {
        var backup = await TryAsync(BackUpAsync, LogBackupFailed);
        var catalogMovies = await TryAsync<int?>(async () => await catalog.WriteAsync(CancellationToken.None), LogCatalogFailed);
        var copy = await TryAsync(() => drive.CopyAsync(CancellationToken.None), LogCopyFailed);
        await TryAsync(() => Task.FromResult(backups.KeepNewest()), (log, e) => LogKeepNewestFailed(log, e, Backups.Kept));

        var report = new CloseOutReport(night, backup, catalogMovies, copy);
        LogWhatItCameTo(report);
        return report;
    }

    // The checklist names the backup by its file name. CreateAsync answers null only when there's no database yet,
    // which a running register always has, so that's reported as not saved too.
    private async Task<string?> BackUpAsync() =>
        await backups.CreateAsync(CancellationToken.None) is { } path ? Path.GetFileName(path) : null;

    // GEN-7, OBS-4: whatever a step throws is logged here, once, as a warning with the exception, and the step comes
    // back as not done, so the next one still runs. What a step answers rather than throws, such as no backup drive,
    // is in the log already, from the step itself. No step hears a token, so nothing one throws is a cancel.
    private async Task<T?> TryAsync<T>(Func<Task<T>> step, Action<ILogger, Exception> logFailure)
    {
        try
        {
            return await step();
        }
        catch (Exception e) // GEN-7: whatever went wrong, Close Out goes on to the next step
        {
            logFailure(logger, e);
            return default;
        }
    }

    // A step that wasn't done is named in words, and formatted only for a line that's written (CA1873).
    private void LogWhatItCameTo(CloseOutReport report)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            var copy = report.Copy?.Status.ToString() ?? Failed;
            LogClosedOut(logger, report.Night, report.Backup ?? NotSaved, report.CatalogMovies is null ? NotWritten : Written, copy);
        }
    }

    // Information (OBS-3): a Close Out is a key event, and this is what it came to. A step that wasn't done has its
    // warning above, from the step itself or from TryAsync.
    [LoggerMessage(Level = LogLevel.Information, Message = "Closed out the register: night {Night}, backup {Backup}, phone catalog {Catalog}, copy to the backup drive {Copy}")]
    private static partial void LogClosedOut(ILogger logger, BusinessDayClosingStatus night, string backup, string catalog, string copy);

    // Warnings (OBS-3): handled, since Close Out goes on, but a step wasn't done.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Close Out couldn't back up the database, so what's changed since the last backup is in none yet; the next Close Out makes a new one")]
    private static partial void LogBackupFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Close Out couldn't write the phone catalog, so the outbox keeps the last one written; the next Close Out writes a new one")]
    private static partial void LogCatalogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Close Out couldn't copy the backups to the backup drive, such as when another program has one of them open on the register; the next Close Out copies whatever the drive is missing")]
    private static partial void LogCopyFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Close Out couldn't delete the backups past the newest {Kept}; the next Close Out tries again")]
    private static partial void LogKeepNewestFailed(ILogger logger, Exception exception, int kept);
}
