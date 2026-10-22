using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rewind.Core.Data;

/// <summary>
/// Stores DateTimes as UTC and marks them UTC on the way back, because SQLite returns them
/// with DateTimeKind.Unspecified (DATA-8).
/// </summary>
public sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
    value => value.ToUniversalTime(),
    stored => DateTime.SpecifyKind(stored, DateTimeKind.Utc));
