namespace Rewind.UI.Errors;

/// <summary>Where the clerk ends up once they close the Windows 98 error dialog (OBS-5).</summary>
public enum CrashOutcome
{
    /// <summary>Checkout opens: the usual way out of a crash.</summary>
    BackToCheckout,

    /// <summary>
    /// Checkout itself crashed, so it stays closed until the clerk presses an F-key. Opening it
    /// again at once could only crash it again.
    /// </summary>
    CheckoutClosed,

    /// <summary>
    /// Rewind shuts down, because the shell crashed before it had even started, and a new one
    /// would only crash again.
    /// </summary>
    ShutDown,
}
