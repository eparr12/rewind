using System.Diagnostics.CodeAnalysis;
using System.IO.Ports;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Rewind.Core.Devices;

/// <summary>
/// Reads the barcode scanner over its USB serial port for as long as Rewind runs (HW-3). It
/// finds the port by the scanner's USB IDs (HW-2) and hands every line to
/// <see cref="ScannerHub"/>. When the scanner is unplugged, or another program holds its port,
/// the status bar says so and the reader looks again every few seconds (HW-4).
/// </summary>
public sealed partial class SerialScanner(
    IDeviceLocator locator,
    ScannerHub hub,
    IOptions<ScannerOptions> options,
    TimeProvider time,
    ILogger<SerialScanner> logger) : BackgroundService
{
    /// <summary>How long the reader waits before it looks for the scanner again (HW-4).</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private const int BufferSize = 256;
    private const int ReadTimeoutMilliseconds = 1000;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan PresenceCheckInterval = TimeSpan.FromSeconds(2);

    private readonly ScannerOptions _options = options.Value;

    // After an unexpected failure, later ones go unlogged until the scanner is ready again, so
    // a problem that lasts all night writes one entry instead of one every few seconds.
    private bool _failing;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Since .NET 10, BackgroundService runs all of this on the thread pool, so a slow first
        // WMI search can't hold up host.Start() and the window (HW-3).
        LogLooking(logger, _options.VendorId, _options.ProductId);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReadAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return; // Shut Down
            }
            catch (Exception e) // GEN-7, HW-4: a scanner problem must never stop Rewind
            {
                hub.SetStatus(ScannerStatus.Missing);
                if (!_failing)
                {
                    _failing = true;
                    LogFailed(logger, e);
                }
            }

            await Task.Delay(RetryDelay, time, stoppingToken);
        }
    }

    private async Task ConnectAndReadAsync(CancellationToken stoppingToken)
    {
        var portName = locator.FindSerialPort(_options.VendorId, _options.ProductId);
        if (portName is null)
        {
            if (hub.SetStatus(ScannerStatus.Missing))
            {
                LogMissing(logger);
            }

            return;
        }

        using var port = NewPort(portName);
        if (!TryOpen(port, out var stream))
        {
            return;
        }

        try
        {
            _failing = false;
            hub.SetStatus(ScannerStatus.Ready);
            LogReady(logger, portName);
            await ReadUntilUnpluggedAsync(port, stoppingToken);
        }
        finally
        {
            Release(stream, portName);
        }
    }

    private SerialPort NewPort(string portName) =>
        new(portName, _options.BaudRate)
        {
            // Terminals such as PuTTY raise DTR and RTS when they open a port, but SerialPort
            // doesn't, and some USB serial devices stay silent until both are up.
            DtrEnable = true,
            RtsEnable = true,

            // Read only ever asks for bytes that BytesToRead says have arrived, so it never has to
            // wait. The timeout guards against a driver whose count is wrong: a Read left waiting
            // throws TimeoutException, which TryReadWaitingBytes handles as a lost scanner, so the
            // reader reconnects instead of hanging while the status bar says ready.
            ReadTimeout = ReadTimeoutMilliseconds,
        };

    // A port that won't open leaves the scanner busy or missing, and the reader tries again in a
    // few seconds.
    private bool TryOpen(SerialPort port, [NotNullWhen(true)] out Stream? stream)
    {
        try
        {
            port.Open();
            stream = port.BaseStream;
            return true;
        }
        catch (UnauthorizedAccessException e) // Windows refuses a port another program has open
        {
            if (hub.SetStatus(ScannerStatus.Busy))
            {
                LogBusy(logger, e, port.PortName);
            }
        }
        catch (IOException e) // the port went away after the search found it, usually in an unplug
        {
            Lost(port.PortName, e);
        }

        stream = null;
        return false;
    }

    // Polls instead of awaiting BaseStream.ReadAsync: a pending serial read only checks its
    // cancellation token before it starts, so it could miss Shut Down. This loop notices both
    // Shut Down and an unplug within moments.
    private async Task ReadUntilUnpluggedAsync(SerialPort port, CancellationToken stoppingToken)
    {
        var lines = new ScanLineReader();
        var buffer = new byte[BufferSize];
        var lastCheck = time.GetTimestamp();
        while (TryReadWaitingBytes(port, buffer, out var count))
        {
            if (count > 0)
            {
                foreach (var line in lines.Feed(buffer.AsSpan(0, count)))
                {
                    hub.Receive(line);
                }

                continue; // the rest of a burst may already be waiting
            }

            if (time.GetElapsedTime(lastCheck) >= PresenceCheckInterval)
            {
                if (!IsPluggedIn(port.PortName))
                {
                    Lost(port.PortName, exception: null);
                    return;
                }

                lastCheck = time.GetTimestamp();
            }

            await Task.Delay(PollInterval, time, stoppingToken);
        }
    }

    // Reads only what has already arrived, so the call returns at once. False once the scanner
    // is gone: after an unplug, Windows refuses the call, or .NET has already marked the port
    // closed, which shows up as InvalidOperationException. A Read that times out (see NewPort)
    // counts as gone too, so it's logged as a warning rather than a reader failure.
    private bool TryReadWaitingBytes(SerialPort port, byte[] buffer, out int count)
    {
        try
        {
            var waiting = port.BytesToRead;
            count = waiting == 0 ? 0 : port.Read(buffer, 0, Math.Min(waiting, buffer.Length));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
        {
            Lost(port.PortName, e);
            count = 0;
            return false;
        }
    }

    // Windows lists every serial port that exists right now, and an unplugged scanner's port
    // leaves the list, even when a driver never fails the reads.
    private static bool IsPluggedIn(string portName) =>
        SerialPort.GetPortNames().Contains(portName, StringComparer.OrdinalIgnoreCase);

    private void Lost(string portName, Exception? exception)
    {
        if (hub.SetStatus(ScannerStatus.Missing))
        {
            LogLost(logger, exception, portName);
        }
    }

    // After an unplug, .NET marks the port closed without releasing it, so SerialPort.Dispose
    // skips it and BaseStream can't be fetched any more. Disposing the stream kept from Open
    // releases the port now, rather than whenever the garbage collector runs. Its finally
    // always closes the handle, but once the device is gone it also throws.
    private void Release(Stream stream, string portName)
    {
        try
        {
            stream.Dispose();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogReleasedAfterUnplug(logger, e, portName);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Looking for the scanner at USB VID_{VendorId} PID_{ProductId}")]
    private static partial void LogLooking(ILogger logger, string vendorId, string productId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scanner ready on {Port}")]
    private static partial void LogReady(ILogger logger, string port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The scanner isn't plugged in")]
    private static partial void LogMissing(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Found the scanner on {Port}, but the port won't open; another program, such as PuTTY, may have it")]
    private static partial void LogBusy(ILogger logger, Exception exception, string port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lost the scanner on {Port}; it was probably unplugged")]
    private static partial void LogLost(ILogger logger, Exception? exception, string port);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Port} threw while closing after an unplug; it's closed anyway")]
    private static partial void LogReleasedAfterUnplug(ILogger logger, Exception exception, string port);

    [LoggerMessage(Level = LogLevel.Error, Message = "The scanner reader failed; it tries again every few seconds and logs nothing more until the scanner is ready")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
