namespace Rewind.Core.Metadata;

/// <summary>What UPCitemdb said about one factory barcode.</summary>
/// <param name="Status">Whether it named the product (NET-12).</param>
/// <param name="ProductTitle">
/// The product's title as UPCitemdb gave it, such as "Tommy Boy (DVD)", trimmed and capped (SEC-5).
/// Null unless the status is <see cref="UpcStatus.Found"/>.
/// </param>
/// <param name="Reason">Why UPCitemdb couldn't answer. Null unless the status is <see cref="UpcStatus.TryLater"/>.</param>
/// <param name="RetryAfter">
/// For <see cref="UpcTryLaterReason.DailyLimit"/>, how long until the day's lookups come back, as
/// UPCitemdb's Retry-After header said. Null for anything else, or when it didn't say.
/// </param>
public sealed record UpcAnswer(UpcStatus Status, string? ProductTitle, UpcTryLaterReason? Reason, TimeSpan? RetryAfter);
