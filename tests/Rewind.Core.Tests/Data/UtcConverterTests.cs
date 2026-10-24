using Rewind.Core.Data;

namespace Rewind.Core.Tests.Data;

public sealed class UtcConverterTests
{
    [Fact]
    public void ConvertFromProvider_UnspecifiedTime_MarksItUtc()
    {
        var converter = new UtcConverter();
        var stored = new DateTime(2026, 10, 24, 2, 15, 0, DateTimeKind.Unspecified);

        var read = Assert.IsType<DateTime>(converter.ConvertFromProvider(stored));

        Assert.Equal(DateTimeKind.Utc, read.Kind);
        Assert.Equal(stored.Ticks, read.Ticks);
    }

    [Fact]
    public void ConvertToProvider_UnspecifiedTime_Throws()
    {
        var converter = new UtcConverter();
        var anyZone = new DateTime(2026, 10, 23, 22, 15, 0, DateTimeKind.Unspecified);

        Assert.Throws<ArgumentException>(() => converter.ConvertToProvider(anyZone));
    }

    [Fact]
    public void ConvertToProvider_LocalTime_ConvertsToUtc()
    {
        var converter = new UtcConverter();
        var movieNight = new DateTime(2026, 10, 24, 2, 15, 0, DateTimeKind.Utc);

        // ToLocalTime uses this machine's time zone; the round trip holds in any of them.
        var stored = Assert.IsType<DateTime>(converter.ConvertToProvider(movieNight.ToLocalTime()));

        Assert.Equal(DateTimeKind.Utc, stored.Kind);
        Assert.Equal(movieNight, stored);
    }
}
