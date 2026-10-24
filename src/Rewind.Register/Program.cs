using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rewind.Core;
using Rewind.Core.Devices;
using Rewind.Core.Sounds;
using Rewind.Register.Devices;
using Rewind.Register.Sounds;
using Rewind.UI;
using Rewind.UI.Dialogs;
using Rewind.UI.Errors;
using Serilog;
using Serilog.Events;

namespace Rewind.Register;

internal static class Program
{
    // OBS-2: one file a day, such as rewind-20261023.log, each kept for 30 days. The store
    // doesn't open every day, so the limit is days, not a number of files.
    private const string LogFile = "rewind-.log";
    private const string LogLine =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    // The exit code when Rewind can't start, which tells the launcher to leave it down rather
    // than bring the same box back (Build Plan 6.2). Not 1: Task Manager's End task and
    // taskkill /F leave 1, and after those the launcher should start Rewind again.
    private const int CouldNotStart = 2;

    private static readonly TimeSpan LogsKept = TimeSpan.FromDays(30);

    [STAThread]
    private static void Main()
    {
        // OBS-5: exceptions on the WinForms thread go to Application.ThreadException instead
        // of WinForms' own error box. This has to come before any window exists.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        ApplicationConfiguration.Initialize();

        // Task Scheduler starts Rewind in C:\Windows\System32, so settings are
        // read from the app's own folder rather than the current one.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        var services = builder.Services;
        services.AddRewindCore(builder.Configuration); // first, so the database is ready before anything else starts
        services.AddRewindScanner(builder.Configuration); // then the devices, once the database is ready
        services.AddOptions<DisplayOptions>()
            .Bind(builder.Configuration.GetSection(DisplayOptions.SectionName))
            .Validate(display => display.Zoom >= DisplayOptions.NormalZoom, "Display:Zoom must be 1 or more: 1 for normal size, 2 for double.")
            .ValidateOnStart(); // NET-11
        services.AddOptions<LogOptions>()
            .Bind(builder.Configuration.GetSection(LogOptions.SectionName))
            .Validate(log => Enum.IsDefined(log.MinimumLevel), "Log:MinimumLevel must be Debug, Information, Warning or Error.")
            .ValidateOnStart(); // NET-11
        services.AddSerilog((provider, logging) => ConfigureLogging(
            logging,
            provider.GetRequiredService<IOptions<RewindPaths>>().Value,
            provider.GetRequiredService<IOptions<LogOptions>>().Value));
        services.AddSingleton<ISoundPlayer, NAudioPlayer>();
        services.AddSingleton<IDeviceLocator, WmiDeviceLocator>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<ErrorHub>();
        services.AddWindowsFormsBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        services.AddSingleton<MainForm>();

        using var host = TryBuild(builder);
        if (host is null || !TryStart(host))
        {
            Environment.ExitCode = CouldNotStart;
            return;
        }

        // Shut Down calls StopApplication, and this then stops every hosted service, such as
        // the scanner's reader. MainForm closes only once they all have (ApplicationStopped),
        // so when Main ends and disposes the host, nothing is left running. A failed stop is
        // logged by the host itself.
        _ = host.WaitForShutdownAsync();

        // WinForms owns this thread until the window closes; then the host is disposed.
        Application.Run(host.Services.GetRequiredService<MainForm>());
    }

    // Building the host builds the logger, from the Paths and Log settings, so when one of
    // those is what's wrong, nothing can be logged and only the box says why.
    private static IHost? TryBuild(HostApplicationBuilder builder)
    {
        try
        {
            return builder.Build();
        }
        catch (Exception e) // OBS-5: whatever it is, Rewind can't start
        {
            ShowStartupError(e);
            return null;
        }
    }

    private static bool TryStart(IHost host)
    {
        // OBS-5: exceptions outside Blazor end up here. Blazor's own never do, because
        // BlazorWebView doesn't pass them on; the ErrorBoundaries in the shell and App catch
        // those.
        var errors = host.Services.GetRequiredService<ErrorHub>();
        Application.ThreadException += (_, e) => errors.Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => errors.Fatal(e.ExceptionObject as Exception);

        try
        {
            host.Start(); // checks the settings (NET-11), then backs up and migrates the database
            return true;
        }
        catch (Exception e) // OBS-5: whatever it is, Rewind can't start
        {
            errors.CouldNotStart(e);
            ShowStartupError(e);
            return false;
        }
    }

    // OBS-5: when Rewind can't start, such as with a setting that fails validation, the reason
    // goes in a plain message box, UI-5's one exception, since the shell that shows Rewind's own
    // dialogs never started. Otherwise Rewind would just close, and only Event Viewer would say why.
    private static void ShowStartupError(Exception exception) =>
        MessageBox.Show(
            $"Rewind can't start:\n\n{exception.Message}",
            Store.Name,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

    // OBS-3: Rewind's own entries from Log:MinimumLevel up, but only warnings and errors from
    // Microsoft's libraries, because EF Core logs every SQL command as Information. The host's
    // lifetime lines stay, so each start and Shut Down is easy to find in the file.
    private static void ConfigureLogging(LoggerConfiguration logging, RewindPaths paths, LogOptions log) =>
        logging
            .MinimumLevel.Is(log.MinimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .WriteTo.File(
                Path.Combine(paths.Logs, LogFile),
                outputTemplate: LogLine,
                formatProvider: CultureInfo.InvariantCulture, // the same file on any PC (CA1305)
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: null, // Serilog keeps 31 files unless told otherwise
                retainedFileTimeLimit: LogsKept);
}
