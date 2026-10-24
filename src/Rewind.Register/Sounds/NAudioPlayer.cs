using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Rewind.Core.Sounds;

namespace Rewind.Register.Sounds;

/// <summary>
/// Plays the register's sounds with NAudio. Each sound gets its own WasapiPlayer, so a scan
/// beep can start while the chime is still ringing (UI-7). The WAV files ship in the app's
/// Sounds folder, one per <see cref="Sound"/>, such as Chime.wav. ADR-0004 explains the choice.
/// </summary>
/// <remarks>
/// Play is called from the renderer's thread for a dialog and from the serial reader's for a
/// scan, but opening a player takes about 60 ms, and about half a second once the speakers
/// have been quiet for a few seconds and gone to sleep. So Play hands that work to the thread
/// pool and returns at once: the spinner's ticks never stall the screen, and a scan never waits
/// on a beep. Each player then plays on a thread of its own and raises PlaybackStopped there,
/// so the clips are only ever read, and the failure flag only changes through Interlocked.
/// </remarks>
internal sealed partial class NAudioPlayer(ILogger<NAudioPlayer> logger) : ISoundPlayer
{
    private const string SoundsFolder = "Sounds";

    private readonly ILogger<NAudioPlayer> _logger = logger;

    // Read once, so a scan never waits on the disk.
    private readonly Dictionary<Sound, byte[]> _clips = LoadClips(logger);

    // 1 from a failure until a sound plays to its end. Only the first failure is a warning, and
    // the rest are logged at Debug, so a night without speakers writes two lines at the usual
    // level instead of one per scan, and Debug still has every one.
    private int _failing;

    public void Play(Sound sound)
    {
        if (!_clips.TryGetValue(sound, out var clip))
        {
            return; // no file for this sound, which LoadClips logged
        }

        _ = Task.Run(() => Guarded(sound, () => Start(sound, clip)));
    }

    // Everything this class runs off its caller's thread runs in here. An exception that got
    // past it would fault a Task that nobody awaits, unseen (OBS-4), or end the whole process
    // from NAudio's playback thread. GEN-7: a sound is never worth stopping a sale.
    private void Guarded(Sound sound, Action work)
    {
        try
        {
            work();
        }
        catch (Exception e)
        {
            Failed(sound, e);
        }
    }

    // Each player disposes itself and its reader when the sound ends. A new one opens whichever
    // speakers are the default right now, so speakers plugged in mid-evening need no restart
    // (GEN-7, HW-4). Shared mode lets sounds overlap: an exclusive player would refuse the second.
    private void Start(Sound sound, byte[] clip)
    {
        var reader = new WaveFileReader(new MemoryStream(clip, writable: false));
        var output = new WasapiPlayerBuilder().WithSharedMode().Build();
        output.PlaybackStopped += (_, e) => Guarded(sound, () => Finished(sound, output, reader, e.Exception));
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

    private void Finished(Sound sound, WasapiPlayer output, WaveFileReader reader, Exception? failure)
    {
        output.Dispose();
        reader.Dispose();
        if (failure is null)
        {
            Played();
        }
        else
        {
            Failed(sound, failure); // such as speakers unplugged mid-sound
        }
    }

    private void Failed(Sound sound, Exception exception)
    {
        if (Interlocked.Exchange(ref _failing, 1) == 0)
        {
            LogNotPlayed(_logger, exception, sound);
        }
        else
        {
            LogStillNotPlaying(_logger, exception, sound);
        }
    }

    private void Played()
    {
        if (Interlocked.Exchange(ref _failing, 0) == 1)
        {
            LogPlayingAgain(_logger);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't play the {Sound} sound; until a sound plays again, failures are logged at Debug")]
    private static partial void LogNotPlayed(ILogger logger, Exception exception, Sound sound);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Still can't play sounds: the {Sound} sound failed too")]
    private static partial void LogStillNotPlaying(ILogger logger, Exception exception, Sound sound);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sounds are playing again")]
    private static partial void LogPlayingAgain(ILogger logger);
}
