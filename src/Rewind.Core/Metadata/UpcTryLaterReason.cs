namespace Rewind.Core.Metadata;

/// <summary>Why UPCitemdb couldn't answer, so the log and the clerk's prompt can say.</summary>
public enum UpcTryLaterReason
{
    /// <summary>
    /// UPCitemdb said Rewind was asking too fast: its free plan allows about 6 lookups a minute.
    /// Rewind then asks nothing for a minute, since asking again only makes UPCitemdb wait longer.
    /// </summary>
    TooFast,

    /// <summary>
    /// The free plan's 100 lookups for the day are used up. Rewind then asks nothing until
    /// UPCitemdb's Retry-After says they're back, or for a minute when it doesn't say.
    /// </summary>
    DailyLimit,

    /// <summary>
    /// UPCitemdb didn't answer: the register is offline, the answer took longer than 10 seconds,
    /// or something else answered instead, such as a Wi-Fi sign-in page.
    /// </summary>
    Offline,

    /// <summary>
    /// UPCitemdb answered with trouble, or something else answered for it: a server error, a
    /// refusal Rewind doesn't expect, an answer that isn't UPCitemdb's own JSON, such as another
    /// server's 404, or one it can't read, such as one that's cut off or too big.
    /// </summary>
    ServerError,
}
