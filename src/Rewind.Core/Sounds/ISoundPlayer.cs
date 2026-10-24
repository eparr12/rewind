namespace Rewind.Core.Sounds;

/// <summary>
/// Plays the register's sounds. Rewind.Register implements it with NAudio (ARCH-4).
/// </summary>
/// <remarks>
/// Any thread may call <see cref="Play"/>, several at once: ScannerHub plays the scan beep on
/// the serial reader's thread while a dialog plays the error chord on the renderer's.
/// </remarks>
public interface ISoundPlayer
{
    /// <summary>
    /// Starts a sound and returns at once, so sounds can overlap and no caller waits on the
    /// speakers. The sound begins a moment later, so two sounds started within a few
    /// milliseconds of each other may begin in either order. A sound that can't play is logged
    /// and skipped, never thrown: missing speakers never stop a sale (GEN-7).
    /// </summary>
    void Play(Sound sound);
}
