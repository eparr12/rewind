namespace Rewind.Core.Tests;

public sealed class RewindPathsTests
{
    // A Debug build refuses the register's folder however Paths__Root spells it, so a capital
    // letter or a slash can't take a test to the store's real data.
    [Theory]
    [InlineData(@"C:\Rewind")]
    [InlineData(@"c:\rewind")]
    [InlineData(@"C:\Rewind\")]
    [InlineData("C:/Rewind/")]
    [InlineData(@"C:\\Rewind")]
    public void IsRegisterRoot_TheRegistersFolder_ReturnsTrue(string root) =>
        Assert.True(RewindPaths.IsRegisterRoot(root));

    // Any other folder is a test folder, even one whose name starts the same.
    [Theory]
    [InlineData(@"C:\RewindDev")]
    [InlineData(@"C:\Rewind\Dev")]
    [InlineData(@"D:\Rewind")]
    [InlineData(@"C:\")]
    public void IsRegisterRoot_AnyOtherFolder_ReturnsFalse(string root) =>
        Assert.False(RewindPaths.IsRegisterRoot(root));

    // Split would throw a NullReferenceException, which says nothing about whose null it was.
    [Fact]
    public void IsRegisterRoot_Null_IsRefused()
    {
        // NET-1: null on purpose, past the compiler's check, since this tests what IsRegisterRoot does with one.
        Action check = () => RewindPaths.IsRegisterRoot(null!);

        var refusal = Assert.Throws<ArgumentNullException>(check);
        Assert.Equal("root", refusal.ParamName);
    }
}
