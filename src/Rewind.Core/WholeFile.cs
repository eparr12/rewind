namespace Rewind.Core;

/// <summary>
/// Writes a file that's whole or not there: in full, straight through to the disk, under another name, which then
/// takes the file's place. Neither a crash nor a power cut leaves half a file, which would look whole to anything that
/// only checks the file is there. Posters, the phone catalog and the copies on the backup drive are all written this
/// way (GEN-8).
/// </summary>
internal static class WholeFile
{
    /// <summary>
    /// Ends the name a file is written under until it's whole, such as 948.jpg.partial. A crash or a power cut can
    /// leave one behind, which the next write of the same file replaces.
    /// </summary>
    public const string PartialExtension = ".partial";

    // Straight through to the disk, so a power cut can't lose what a crash wouldn't. Unbuffered, since a whole file is
    // written at once.
    private static readonly FileStreamOptions WriteThrough = new()
    {
        Mode = FileMode.Create,
        Access = FileAccess.Write,
        Share = FileShare.None,
        Options = FileOptions.WriteThrough | FileOptions.Asynchronous,
        BufferSize = 0,
    };

    /// <summary>
    /// Writes the file, replacing any it held before. A write that fails or is cancelled deletes its new file, and
    /// leaves the old one as it was.
    /// </summary>
    /// <param name="path">The file, whose folder must be there already.</param>
    /// <param name="bytes">All of the file.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public static async Task WriteAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var partial = path + PartialExtension;
        try
        {
            await using (var file = new FileStream(partial, WriteThrough))
            {
                await file.WriteAsync(bytes, cancellationToken);
            }

            File.Move(partial, path, overwrite: true);
        }
        finally
        {
            File.Delete(partial); // gone already after the move; does nothing then
        }
    }
}
