using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Rewind.UI.Errors;

/// <summary>
/// The status bar's own ErrorBoundary. Without it, what the bar throws reaches App's, which replaces the
/// whole shell, and a sale on Checkout would go with it over a status line. This one closes only the bar,
/// and sends the exception to <see cref="ErrorHub"/>, so the log names the bar (OBS-3) and the shell shows
/// the error dialog over whatever is open (OBS-5).
/// </summary>
public sealed class StatusBarBoundary : ErrorBoundary
{
    [Inject]
    private ErrorHub Errors { get; set; } = null!;

    // In place of ErrorBoundary's own, which would log the exception a second time (OBS-4).
    protected override Task OnErrorAsync(Exception exception)
    {
        Errors.ReportFromComponent(nameof(StatusBar), exception);
        return Task.CompletedTask;
    }
}
