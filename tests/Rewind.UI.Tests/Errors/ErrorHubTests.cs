using Microsoft.Extensions.Logging;
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

    // #35: a screen the clerk has left has no ErrorBoundary, so the shell shows its error instead.
    [Fact]
    public void ReportFromClosedScreen_AnyException_RaisesCrashed()
    {
        var errors = new ErrorHub(NullLogger<ErrorHub>.Instance);
        var crashed = false;
        errors.Crashed += () => crashed = true;

        errors.ReportFromClosedScreen("Intake", new InvalidOperationException("Test crash"));

        Assert.True(crashed);
    }

    // OBS-3: the log names the screen, so a reader years on doesn't have to work it out from the stack trace.
    [Fact]
    public void ReportFromClosedScreen_AnyException_LogsTheScreen()
    {
        var log = new ListLogger<ErrorHub>();
        var crash = new InvalidOperationException("Test crash");

        new ErrorHub(log).ReportFromClosedScreen("Intake", crash);

        Assert.Equal([(LogLevel.Error, "Unhandled exception from Intake after the clerk left it; going back to Checkout", crash)], log.Entries);
    }
}
