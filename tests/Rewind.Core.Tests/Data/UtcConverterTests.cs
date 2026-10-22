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
}
