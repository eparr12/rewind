using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Rewind.Core.Metadata;

/// <summary>
/// The movies' posters on the register, each named for its movie's TMDB ID, never its title
/// (SEC-5), such as 948.jpg: data\posters holds them at the register's size and data\thumbs at
/// the phone catalog's (Build Plan 2.3). They aren't in the database or its backups, so after a
/// restore they're downloaded again.
/// </summary>
public sealed partial class Posters(IOptions<RewindPaths> options, ILogger<Posters> logger)
{
    private const string PartialExtension = ".partial";

    // Each write goes straight through to the disk, so a power cut can't lose what a crash
    // wouldn't. Unbuffered, since a whole poster is written at once.
    private static readonly FileStreamOptions WriteThrough = new()
    {
        Mode = FileMode.Create,
        Access = FileAccess.Write,
        Share = FileShare.None,
        Options = FileOptions.WriteThrough | FileOptions.Asynchronous,
        BufferSize = 0,
    };

    private readonly RewindPaths _paths = options.Value;

    /// <summary>The file name a movie's poster is saved under, at every size, such as 948.jpg.</summary>
    public static string FileNameFor(int tmdbId) =>
        string.Create(CultureInfo.InvariantCulture, $"{tmdbId}.jpg");

    /// <summary>
    /// Saves a movie's poster at one size, replacing any saved before. It's written in full to a
    /// new file that then takes the old one's place, so neither a crash nor a power cut leaves half
    /// a poster, which would look whole to anything that only checks the file is there. A save that
    /// fails or is cancelled deletes its new file.
    /// </summary>
    /// <param name="tmdbId">The movie's TMDB ID, which names the file.</param>
    /// <param name="size"><see cref="PosterSize.Register"/> or <see cref="PosterSize.Thumb"/>.</param>
    /// <param name="jpeg">The poster, as <see cref="TmdbClient.GetPosterAsync"/> downloaded it.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    public async Task SaveAsync(int tmdbId, PosterSize size, byte[] jpeg, CancellationToken cancellationToken)
    {
        var folder = FolderFor(size);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileNameFor(tmdbId));
        var partial = path + PartialExtension;
        try
        {
            await using (var file = new FileStream(partial, WriteThrough))
            {
                await file.WriteAsync(jpeg, cancellationToken);
            }

            File.Move(partial, path, overwrite: true);
        }
        finally
        {
            File.Delete(partial); // gone already after the move; does nothing then
        }
    }

    /// <summary>
    /// Reads a movie's saved poster at one size. Returns null when there's none, or when it can't
    /// be read, such as when the data folder's permissions are wrong, so the screen shows the movie
    /// without its poster (GEN-7).
    /// </summary>
    public async Task<byte[]?> ReadAsync(int tmdbId, PosterSize size, CancellationToken cancellationToken)
    {
        var path = Path.Combine(FolderFor(size), FileNameFor(tmdbId));
        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null; // never downloaded, or not yet downloaded again after a restore
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogUnreadable(logger, e, path);
            return null;
        }
    }

    private string FolderFor(PosterSize size) => size switch
    {
        PosterSize.Register => _paths.Posters,
        PosterSize.Thumb => _paths.Thumbs,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Only the register's and the phone catalog's posters are saved."),
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't read the poster {Path}, so the movie is shown without it")]
    private static partial void LogUnreadable(ILogger logger, Exception exception, string path);
}
