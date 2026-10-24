using Rewind.Core.Metadata;

namespace Rewind.Core.Tests.Metadata;

public sealed class UpcItemDbOptionsTests
{
    [Fact]
    public void IsValidBaseUrl_UpcItemDbsOwn_IsTrue() =>
        Assert.True(UpcItemDbOptions.IsValidBaseUrl(new UpcItemDbOptions().BaseUrl));

    // Each would send lookups somewhere else, or in the clear.
    [Theory]
    [InlineData("http://api.upcitemdb.com/prod/trial/")]
    [InlineData("https://api.upcitemdb.com/prod/trial")] // lookups would go to /prod/lookup
    [InlineData("https://api.upcitemdb.com/prod/trial/?upc=1")]
    [InlineData("/prod/trial/")]
    public void IsValidBaseUrl_AnythingElse_IsFalse(string baseUrl) =>
        Assert.False(UpcItemDbOptions.IsValidBaseUrl(new Uri(baseUrl, UriKind.RelativeOrAbsolute)));
}
