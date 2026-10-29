using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rewind.Core.Data;
using Rewind.Core.Scans;

namespace Rewind.Core.Metadata;

/// <summary>
/// Finds the title for a DVD's factory barcode (Build Plan 2.2). UPCitemdb's free plan allows 100
/// lookups a day, so each answer is saved in UpcLookups, not found included, and a barcode Rewind
/// has looked up before is answered from there. The title is cleaned as it's read, from the
/// product's title as UPCitemdb gave it, so a better <see cref="ProductTitles"/> cleans old
/// answers too.
/// </summary>
public sealed partial class BarcodeTitles(
    UpcItemDbClient upcItemDb,
    UpcItemDbPacer pacer,
    IDbContextFactory<RewindDb> dbFactory,
    TimeProvider time,
    ILogger<BarcodeTitles> logger) : IBarcodeTitles
{
    /// <summary>
    /// How long a "not found" answer stands before the barcode is asked about again. UPCitemdb keeps
    /// growing, and a not found can come from a bad hour rather than from UPCitemdb not knowing the
    /// barcode, so a month on, it's worth a lookup.
    /// </summary>
    public static readonly TimeSpan NotFoundKeptFor = TimeSpan.FromDays(30);

    /// <inheritdoc />
    public async Task<BarcodeTitle> LookupAsync(string code, IProgress<TimeSpan>? waiting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);

        // SEC-5: only a barcode in Rewind's form is looked up, and that form is UpcLookups' key.
        if (!Upc.TryNormalize(code, out var upc))
        {
            LogNotAUpc(logger, code.Length);
            return new BarcodeTitle(null, UpcStatus.NotFound, null, null, null, null);
        }

        if (await SavedAsync(upc, cancellationToken) is { } saved)
        {
            LogSaved(logger, upc);
            return saved;
        }

        var answer = await pacer.LookupAsync(token => upcItemDb.LookupAsync(upc, token), waiting, cancellationToken);
        if (answer.Status == UpcStatus.TryLater)
        {
            // Not saved, so the next scan asks again.
            return new BarcodeTitle(upc, UpcStatus.TryLater, null, null, answer.Reason, time.GetUtcNow() + answer.RetryAfter);
        }

        return await SaveAsync(upc, answer.ProductTitle, cancellationToken);
    }

    // When two lookups of one barcode race, UpcLookups.Upc's unique index refuses the second new
    // row, and the answer saved first stands.
    private async Task<BarcodeTitle> SaveAsync(string upc, string? productTitle, CancellationToken cancellationToken)
    {
        try
        {
            return await WriteAsync(upc, productTitle, cancellationToken);
        }
        catch (DbUpdateException e) when (SqliteErrors.IsUniqueIndexRefusal(e))
        {
            if (await SavedAsync(upc, cancellationToken) is { } first)
            {
                LogSavedFirst(logger, upc);
                return first;
            }

            throw;
        }
    }

    // One row a barcode, in one SaveChanges (DATA-7). A "not found" old enough to ask again gets the
    // new answer. A row that has a title already, which another lookup saved meanwhile, stands.
    private async Task<BarcodeTitle> WriteAsync(string upc, string? productTitle, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.UpcLookups.SingleOrDefaultAsync(u => u.Upc == upc, cancellationToken);
        if (row is { ProductTitle: { } savedTitle })
        {
            LogSavedFirst(logger, upc);
            return TitleFor(upc, savedTitle);
        }

        var lookedUpAt = time.GetUtcNow().UtcDateTime; // NET-8, DATA-8
        if (row is null)
        {
            db.UpcLookups.Add(new UpcLookup { Upc = upc, ProductTitle = productTitle, LookedUpAt = lookedUpAt });
        }
        else
        {
            row.ProductTitle = productTitle;
            row.LookedUpAt = lookedUpAt;
        }

        await db.SaveChangesAsync(cancellationToken);
        return TitleFor(upc, productTitle);
    }

    // DATA-12: read untracked, straight into a record. Null when the barcode has no row yet, or
    // only a "not found" old enough to ask again.
    private async Task<BarcodeTitle?> SavedAsync(string upc, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var saved = await db.UpcLookups
            .AsNoTracking()
            .Where(u => u.Upc == upc)
            .Select(u => new SavedAnswer(u.ProductTitle, u.LookedUpAt))
            .SingleOrDefaultAsync(cancellationToken);
        if (saved is { ProductTitle: null } && time.GetUtcNow().UtcDateTime - saved.LookedUpAt >= NotFoundKeptFor)
        {
            LogNotFoundLongAgo(logger, upc, saved.LookedUpAt);
            return null;
        }

        return saved is null ? null : TitleFor(upc, saved.ProductTitle);
    }

    // A product's title that's nothing but words like DVD leaves no title to search with.
    private static BarcodeTitle TitleFor(string upc, string? productTitle) =>
        ProductTitles.Clean(productTitle) is { } title
            ? new BarcodeTitle(upc, UpcStatus.Found, productTitle, title, null, null)
            : new BarcodeTitle(upc, UpcStatus.NotFound, productTitle, null, null, null);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Upc} is in UpcLookups, so UPCitemdb isn't asked")]
    private static partial void LogSaved(ILogger logger, string upc);

    [LoggerMessage(Level = LogLevel.Debug, Message = "UPCitemdb didn't know {Upc} on {LookedUpAt}, long enough ago to ask again")]
    private static partial void LogNotFoundLongAgo(ILogger logger, string upc, DateTime lookedUpAt);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Another lookup of {Upc} saved UPCitemdb's answer first; that one stands")]
    private static partial void LogSavedFirst(ILogger logger, string upc);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Didn't look up a code of {Length} characters that isn't a factory barcode")]
    private static partial void LogNotAUpc(ILogger logger, int length);

    private sealed record SavedAnswer(string? ProductTitle, DateTime LookedUpAt);
}
