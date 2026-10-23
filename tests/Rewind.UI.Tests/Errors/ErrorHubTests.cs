using Microsoft.Extensions.Logging.Abstractions;
using Rewind.UI.Errors;

namespace Rewind.UI.Tests.Errors;

public sealed class ErrorHubTests
{
    [Fact]
    public void Report_WinFormsThreadException_RaisesCrashed()
    {
        var errors = new ErrorHub(NullLogger<ErrorHub>.Instance);
        var crashed = false;
        errors.Crashed += () => crashed = true;

        errors.Report(new InvalidOperationException("Test crash"));

        Assert.True(crashed);
    }
}
