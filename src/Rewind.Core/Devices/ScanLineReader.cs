using System.Text;
using Rewind.Core.Scans;

namespace Rewind.Core.Devices;

/// <summary>
/// Splits the scanner's bytes into scans. A scan ends at CR or LF and empty lines are skipped,
/// so CR, LF and CR+LF endings all work. StreamReader.ReadLine can't do this: after a bare CR
/// it waits for the next byte to see whether an LF follows, so a CR-only scanner would always
/// be one scan behind. One reader serves one port, on one thread.
/// </summary>
public sealed class ScanLineReader
{
    /// <summary>
    /// The most characters kept from one line; the rest are dropped until the line ends. It's
    /// longer than any scan <see cref="Barcodes.Classify"/> looks at, so a cut-off line always
    /// comes out unknown, and nothing a scanner sends can grow without limit (SEC-5).
    /// </summary>
    public const int MaxLineLength = Barcodes.MaxLength * 2;

    private const byte Cr = (byte)'\r';
    private const byte Lf = (byte)'\n';
    private const byte FirstPrintable = (byte)' ';
    private const byte LastPrintable = (byte)'~';
    private const char Unreadable = '?';

    private readonly StringBuilder _line = new(MaxLineLength);

    /// <summary>
    /// Takes bytes as they arrive, in chunks of any size, and returns the scans they finish,
    /// oldest first: usually none or one. A byte outside printable ASCII becomes a question
    /// mark, so it shows in the status bar and the scan comes out unknown.
    /// </summary>
    public IReadOnlyList<string> Feed(ReadOnlySpan<byte> received)
    {
        List<string>? finished = null;
        foreach (var b in received)
        {
            if (b is Cr or Lf)
            {
                if (_line.Length > 0)
                {
                    (finished ??= []).Add(_line.ToString());
                    _line.Clear();
                }
            }
            else if (_line.Length < MaxLineLength)
            {
                _line.Append(b is >= FirstPrintable and <= LastPrintable ? (char)b : Unreadable);
            }
        }

        return finished ?? [];
    }
}
