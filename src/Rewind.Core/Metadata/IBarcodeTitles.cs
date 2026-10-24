namespace Rewind.Core.Metadata;

/// <summary>
/// What the Intake screen asks of Core when a DVD's factory barcode is scanned (ARCH-5): the title
/// to search TMDB with. <see cref="BarcodeTitles"/> does it, and the screen's tests use a fake
/// (ADR-0003).
/// </summary>
public interface IBarcodeTitles
{
    /// <summary>
    /// Finds the title for a factory barcode: from UpcLookups when Rewind has looked the barcode up
    /// before, without asking UPCitemdb, and otherwise from UPCitemdb, whose answer is then saved,
    /// not found included. A barcode UPCitemdb didn't know is asked about again once that answer is
    /// <see cref="BarcodeTitles.NotFoundKeptFor"/> old. When UPCitemdb can't answer, nothing is
    /// saved, so the next scan asks again. A code that isn't a factory barcode is not found, without
    /// asking.
    /// </summary>
    /// <param name="code">
    /// The barcode as scanned, such as 032429258373. A 13-digit EAN that starts with 0, such as
    /// 0032429258373, is the same barcode.
    /// </param>
    /// <param name="waiting">
    /// Told how long the lookup must wait for UPCitemdb's burst limit of 6 lookups a minute, before
    /// it waits, then <see cref="TimeSpan.Zero"/> once it's asking, so the screen can say so.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the lookup, such as when the clerk presses Esc. One can take a while: a wait for the
    /// burst limit, then up to 10 seconds for UPCitemdb's answer.
    /// </param>
    Task<BarcodeTitle> LookupAsync(string code, IProgress<TimeSpan>? waiting, CancellationToken cancellationToken);
}
