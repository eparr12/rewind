using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Rewind.Core.Sounds;

namespace Rewind.Register.Sounds;

/// <summary>
/// Plays the register's sounds with NAudio. Each sound gets its own WaveOut, so a scan beep
/// can start while the chime is still ringing (UI-7). The WAV files ship in the app's Sounds
/// folder, one per <see cref="Sound"/>, such as Chime.wav.
/// </summary>
internal sealed partial class NAudioPlayer(ILogger<NAudioPlayer> logger) : ISoundPlayer
{
    private const string SoundsFolder = "Sounds";

    private readonly ILogger<NAudioPlayer> _logger = logger;

    // Read once, so a scan never waits on the disk.
    private readonly Dictionary<Sound, byte[]> _clips = LoadClips(logger);

    // After a failure, later failures go unlogged until a sound plays again, so a night
    // without speakers writes two lines to the log instead of one per scan.
    private volatile bool _failing;

    public void Play(Sound sound)
    {
        if (!_clips.TryGetValue(sound, out var clip))
        {
            return; // no file for this sound, which LoadClips logged
        }

        try
        {
            Start(sound, clip);
            if (_failing)
            {
                _failing = false;
                LogPlayingAgain(_logger);
            }
        }
        catch (Exception e) // GEN-7: a sound is never worth stopping a sale
        {
            if (!_failing)
            {
                _failing = true;
                LogNotPlayed(_logger, e, sound);
            }
        }
    }

    // Each WaveOut disposes itself and its reader when the sound ends.
    private void Start(Sound sound, byte[] clip)
    {
        var reader = new WaveFileReader(new MemoryStream(clip, writable: false));
        var output = new WaveOut();
        output.PlaybackStopped += (_, e) =>
        {
            output.Dispose();
            reader.Dispose();
            if (e.Exception is not null)
            {
                LogNotPlayed(_logger, e.Exception, sound);
            }
        };

        try
        {
            output.Init(reader);
            output.Play();
        }
        catch
        {
            output.Dispose();
            reader.Dispose();
            throw;
        }
    }

    private static Dictionary<Sound, byte[]> LoadClips(ILogger logger)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, SoundsFolder);
        var clips = new Dictionary<Sound, byte[]>();
        foreach (var sound in Enum.GetValues<Sound>())
        {
            var file = Path.Combine(folder, $"{sound}.wav");
            if (!File.Exists(file))
            {
                LogMissing(logger, sound, file);
                continue;
            }

            try
            {
                clips[sound] = File.ReadAllBytes(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LogUnreadable(logger, e, sound, file);
            }
        }

        return clips;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Sound} sound is off: {File} is missing")]
    private static partial void LogMissing(ILogger logger, Sound sound, string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Sound} sound is off: {File} can't be read")]
    private static partial void LogUnreadable(ILogger logger, Exception exception, Sound sound, string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't play the {Sound} sound")]
    private static partial void LogNotPlayed(ILogger logger, Exception exception, Sound sound);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sounds are playing again")]
    private static partial void LogPlayingAgain(ILogger logger);
}
