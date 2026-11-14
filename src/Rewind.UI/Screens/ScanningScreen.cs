using Microsoft.AspNetCore.Components;
using Rewind.Core.Devices;
using Rewind.Core.Scans;
using Rewind.UI.Dialogs;
using Rewind.UI.Errors;

namespace Rewind.UI.Screens;

/// <summary>
/// A screen that takes scans (UI-4), such as Checkout or Intake (ADR-0011). It listens from the moment it
/// opens until the clerk leaves, and the screen itself says only what a scan means to it, in
/// <see cref="OnScanAsync"/>.
/// </summary>
/// <remarks>
/// UI-11: a scan can arrive on the scanner's thread, so it moves onto the renderer's first. An exception in
/// there would only fault the Task that InvokeAsync returns, which nobody awaits, so it goes to
/// DispatchExceptionAsync and the screen's ErrorBoundary. One thrown while the screen is open is handed on in
/// the same turn, so it reaches the ErrorBoundary before the screen can be disposed. Once the clerk has left,
/// DispatchExceptionAsync throws, since the renderer has no component to give it to, so an error then goes to
/// ErrorHub instead, which logs it and shows the error dialog (OBS-5). The cancellation that leaving causes
/// isn't an error, so it goes nowhere.
/// </remarks>
public abstract class ScanningScreen : ComponentBase, IDisposable
{
    // Cancelled when the clerk leaves the screen, so whatever still waits on the database or the web stops.
    private readonly CancellationTokenSource _closing = new();

    protected ScanningScreen() => Closing = _closing.Token;

    /// <summary>
    /// Cancelled once the clerk has left the screen. Read once, as the screen is made, so it can still be read
    /// after the clerk leaves: a call made then is cancelled at once, rather than throwing because the source
    /// behind it is gone.
    /// </summary>
    protected CancellationToken Closing { get; }

    /// <summary>What Rewind is doing while Core answers, such as "Saving the sale...". Null the rest of the time.</summary>
    protected string? Doing { get; set; }

    /// <summary>While Core answers, nothing else starts.</summary>
    protected bool Busy => Doing is not null;

    /// <summary>Whether a dialog is on screen, waiting for the clerk's answer.</summary>
    protected bool DialogOpen => Dialogs.Current is not null;

    // NET-1: Blazor sets every [Inject] property before OnInitialized, so none is read null. The dialogs are
    // private, so a screen shows one only through ShowAsync, which knows when the screen has closed.
    [Inject]
    private DialogService Dialogs { get; set; } = null!;

    [Inject]
    private IScanner Scanner { get; set; } = null!;

    [Inject]
    private ErrorHub Errors { get; set; } = null!;

    public void Dispose()
    {
        Scanner.Scanned -= OnScanned;
        _closing.Cancel();
        _closing.Dispose();

        // There's no finalizer, but CA1816 asks for this in a class built to be inherited: a screen that ever
        // adds one finds it turned off here, without implementing IDisposable again.
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Listening starts here, before any read the screen starts in OnInitializedAsync, so a scan during that
    /// read still reaches the screen. Sealed, so no screen can skip it: a screen starts its own work in
    /// OnInitializedAsync, which Blazor runs after this.
    /// </summary>
    protected sealed override void OnInitialized() => Scanner.Scanned += OnScanned;

    /// <summary>What a scan means to this screen. It runs on the renderer's thread, and never once the clerk has left.</summary>
    protected abstract Task OnScanAsync(Scan scan);

    /// <summary>
    /// Shows what Rewind is doing until Core answers, and returns the answer. Null when the call was cancelled,
    /// by the clerk leaving the screen or by the token the screen passed, such as Intake's Esc. An answer that
    /// arrives after the clerk has left still comes back, since a save that committed is done (NET-7): what the
    /// screen does with it is its own, and only ShowAsync refuses once the screen has closed.
    /// </summary>
    protected async Task<T?> WhileBusyAsync<T>(string doing, Func<CancellationToken, Task<T>> call, CancellationToken token)
        where T : class?
    {
        Doing = doing;
        StateHasChanged(); // drawn now, even for a scan, which Blazor doesn't draw for by itself
        try
        {
            return await call(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            Doing = null;
        }
    }

    /// <summary>
    /// Shows a dialog, the one way a scanning screen shows one. It draws the screen first, so the screen behind
    /// the dialog shows what happened, not what was happening. Once the clerk has left, it shows nothing and
    /// answers as Cancel does. The dialogs outlive the screen, so a dialog for an answer that came after the
    /// clerk left would open over whatever screen is up now, and replace any dialog open there, which would
    /// then count as cancelled.
    /// </summary>
    protected Task<bool> ShowAsync(Dialog dialog)
    {
        if (Closing.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        StateHasChanged();
        return Dialogs.ShowAsync(dialog);
    }

    private void OnScanned(Scan scan) => _ = InvokeAsync(async () =>
    {
        if (Closing.IsCancellationRequested)
        {
            return; // a scan the scanner's thread raised just before the clerk left
        }

        try
        {
            await OnScanAsync(scan);
        }
        catch (OperationCanceledException) when (Closing.IsCancellationRequested)
        {
            // the clerk left while the scan was being answered: not an error (OBS-4)
        }
        catch (Exception e) when (Closing.IsCancellationRequested)
        {
            Errors.ReportFromComponent(GetType().Name, e);
        }
        catch (Exception e)
        {
            await DispatchExceptionAsync(e);
        }
    });
}
