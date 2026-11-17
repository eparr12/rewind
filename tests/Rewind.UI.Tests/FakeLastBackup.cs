using Rewind.Core.Data;

namespace Rewind.UI.Tests;

/// <summary>
/// The last backup as the status bar's tests set it up (TEST-5): what the bar's read answers, or throws, and the news
/// of a backup, which the test raises by hand.
/// </summary>
internal sealed class FakeLastBackup : ILastBackup
{
    public event Action? BackedUp;

    /// <summary>What a read answers: when the newest backup was made, as its name says, or null for none.</summary>
    public DateTime? MadeAt { get; set; }

    /// <summary>What a read throws instead of answering, such as a fault in Core.</summary>
    public Exception? Failure { get; set; }

    /// <summary>
    /// When a backup made while the next read is under way was made. That read answers what was there before, and
    /// the news comes on a thread of its own, as Close Out's does, before the read returns.
    /// </summary>
    public DateTime? MadeWhileReading { get; set; }

    /// <summary>
    /// What a listener threw when the news came on that thread, kept for the test to look at, since a thread that
    /// threw would end the whole test run.
    /// </summary>
    public Exception? ListenerFailure { get; private set; }

    /// <summary>How many times it's been read.</summary>
    public int Reads { get; private set; }

    /// <summary>Whether anything still listens for backups, such as a status bar that never stopped.</summary>
    public bool IsListenedTo => BackedUp is not null;

    public DateTime? NewestMadeAt()
    {
        Reads++;
        var answer = Failure is null ? MadeAt : throw Failure;
        if (MadeWhileReading is { } made)
        {
            MadeWhileReading = null;
            MadeAt = made;
            var closeOut = new Thread(BackUpOnItsOwnThread);
            closeOut.Start();
            closeOut.Join();
        }

        return answer;
    }

    /// <summary>Raises the news of a backup, as the thread that made it would.</summary>
    public void BackUp() => BackedUp?.Invoke();

    /// <summary>
    /// Takes the listeners there are now, for news of a backup made just before one of them stopped listening, which
    /// reaches that listener after it stopped.
    /// </summary>
    public Action RaisedBeforeStopping()
    {
        var listeners = BackedUp;
        return () => listeners?.Invoke();
    }

    private void BackUpOnItsOwnThread()
    {
        try
        {
            BackUp();
        }
        catch (Exception e) // kept for the test, rather than ending the run
        {
            ListenerFailure = e;
        }
    }
}
