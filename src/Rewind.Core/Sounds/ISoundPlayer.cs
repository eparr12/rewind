namespace Rewind.Core.Sounds;

/// <summary>
/// Plays the register's sounds. Rewind.Register implements it with NAudio (ARCH-4).
/// </summary>
public interface ISoundPlayer
{
    /// <summary>
    /// Starts a sound and returns at once, so sounds can overlap. A sound that can't play is
    /// logged and skipped, never thrown: missing speakers never stop a sale (GEN-7).
    /// </summary>
    void Play(Sound sound);
}
