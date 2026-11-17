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
    /// What happens while a read is under way, once it has its answer and before it returns it, such as a backup made
    /// meanwhile. It happens once.
    /// </summary>
    public Action? WhileReading { get; set; }

    /// <summary>How many times it's been read.</summary>
    public int Reads { get; private set; }

    /// <summary>Whether anything still listens for backups, such as a status bar that never stopped.</summary>
    public bool IsListenedTo => BackedUp is not null;

    public DateTime? NewestMadeAt()
    {
        Reads++;
        var answer = Failure is null ? MadeAt : throw Failure;
        var whileReading = WhileReading;
        WhileReading = null;
        whileReading?.Invoke();
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
}
