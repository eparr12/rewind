namespace Rewind.Core.Metadata;

/// <summary>What a DVD's factory barcode says the movie is called, as the Intake screen uses it.</summary>
/// <param name="Upc">
/// The barcode in Rewind's form, 12 or 13 digits, as UpcLookups keeps it and Case.FactoryUpc will.
/// Null when the code isn't a factory barcode, such as one that didn't scan properly.
/// </param>
/// <param name="Status">
/// <see cref="UpcStatus.Found"/> when there's a title to search TMDB with, and
/// <see cref="UpcStatus.NotFound"/> when there isn't, such as when the product's title is only
/// words like DVD.
/// </param>
/// <param name="ProductTitle">
/// The product's title as UPCitemdb gave it, such as "Tommy Boy (DVD)". Null when UPCitemdb
/// didn't name the product.
/// </param>
/// <param name="Title">
/// The title to search TMDB with, such as "Tommy Boy", as <see cref="ProductTitles.Clean"/> leaves
/// the product's. Null unless the status is <see cref="UpcStatus.Found"/>.
/// </param>
/// <param name="Reason">Why UPCitemdb couldn't answer. Null unless the status is <see cref="UpcStatus.TryLater"/>.</param>
/// <param name="RetryAt">
/// For <see cref="UpcTryLaterReason.DailyLimit"/>, when the day's lookups come back, in UTC, as
/// UPCitemdb said. Null for anything else, or when it didn't say.
/// </param>
public sealed record BarcodeTitle(
    string? Upc,
    UpcStatus Status,
    string? ProductTitle,
    string? Title,
    UpcTryLaterReason? Reason,
    DateTimeOffset? RetryAt);
