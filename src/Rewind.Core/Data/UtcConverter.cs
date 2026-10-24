using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rewind.Core.Data;

/// <summary>
/// Stores DateTimes as UTC and marks them UTC on the way back, because SQLite returns them
/// with DateTimeKind.Unspecified (DATA-8).
/// </summary>
public sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
    value => ToUtc(value),
    stored => DateTime.SpecifyKind(stored, DateTimeKind.Utc))
{
    // ToUniversalTime treats an Unspecified time as local and shifts it by the register's
    // offset, which would store a wrong time without a word. Times read from TimeProvider
    // (NET-8) are always Utc or Local, so an Unspecified one is a bug.
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => throw new ArgumentException(
            "A DateTime with DateTimeKind.Unspecified could be in any time zone, so it can't be stored. " +
            "Read the time from TimeProvider (NET-8).",
            nameof(value)),
    };
}
