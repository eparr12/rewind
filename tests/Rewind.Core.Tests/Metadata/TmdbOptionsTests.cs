using Rewind.Core.Metadata;

namespace Rewind.Core.Tests.Metadata;

public sealed class TmdbOptionsTests
{
    // An empty token means TMDB isn't set up, which Rewind starts without (GEN-7).
    [Theory]
    [InlineData("")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiJ0ZXN0In0.c2lnbmF0dXJl")]
    public void IsWellFormed_EmptyOrToken_IsTrue(string token) =>
        Assert.True(TmdbOptions.IsWellFormed(token));

    // Each would make an Authorization header TMDB refuses, or HttpClient can't send.
    [Theory]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiJ9")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9\n")]
    [InlineData(" eyJhbGciOiJIUzI1NiJ9")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9é")]
    public void IsWellFormed_SpacesOrOtherCharacters_IsFalse(string token) =>
        Assert.False(TmdbOptions.IsWellFormed(token));
}
