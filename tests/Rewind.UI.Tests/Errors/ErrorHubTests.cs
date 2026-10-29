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

    // The line says only what ErrorHub does: going back to Checkout is the shell's doing.
    [Fact]
    public void Report_WinFormsThreadException_LogsIt()
    {
        var log = new ListLogger<ErrorHub>();
        var crash = new InvalidOperationException("Test crash");

        new ErrorHub(log).Report(crash);

        Assert.Equal([(LogLevel.Error, "Unhandled exception on the WinForms thread; reported to the shell", crash)], log.Entries);
    }

    // #35: a screen the clerk has left has no ErrorBoundary, and the status bar's only one is App's,
    // which would replace the whole shell over a lookup. So the shell shows their errors instead.
    [Fact]
    public void ReportFromComponent_AnyException_RaisesCrashed()
    {
        var errors = new ErrorHub(NullLogger<ErrorHub>.Instance);
        var crashed = false;
        errors.Crashed += () => crashed = true;

        errors.ReportFromComponent("StatusBar", new InvalidOperationException("Test crash"));

        Assert.True(crashed);
    }

    // OBS-3: the log names the component, so a reader years on doesn't have to work it out from the
    // stack trace, and says only what ErrorHub does, since there may be no shell by then.
    [Fact]
    public void ReportFromComponent_AnyException_LogsTheComponent()
    {
        var log = new ListLogger<ErrorHub>();
        var crash = new InvalidOperationException("Test crash");

        new ErrorHub(log).ReportFromComponent("StatusBar", crash);

        Assert.Equal([(LogLevel.Error, "Unhandled exception from StatusBar; reported to the shell", crash)], log.Entries);
    }
}
