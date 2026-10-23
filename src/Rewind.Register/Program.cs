using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rewind.Core;
using Rewind.Core.Devices;
using Rewind.Core.Sounds;
using Rewind.Register.Devices;
using Rewind.Register.Sounds;
using Rewind.UI.Dialogs;
using Rewind.UI.Errors;
using Serilog;
using Serilog.Events;

namespace Rewind.Register;

internal static class Program
{
    // OBS-2: one file a day, such as rewind-20261023.log, kept for 30 days.
    private const string LogFile = "rewind-.log";
    private const int LogFilesKept = 30;
    private const string LogLine =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

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
        services.AddSerilog((provider, logging) =>
            ConfigureLogging(logging, provider.GetRequiredService<IOptions<RewindPaths>>().Value));
        services.AddSingleton<ISoundPlayer, NAudioPlayer>();
        services.AddSingleton<IDeviceLocator, WmiDeviceLocator>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<ErrorHub>();
        services.AddWindowsFormsBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        services.AddSingleton<MainForm>();

        using var host = builder.Build();

        // OBS-5: exceptions outside Blazor end up here. They're hooked up before the host
        // starts, so a failed migration at startup is logged too.
        var errors = host.Services.GetRequiredService<ErrorHub>();
        Application.ThreadException += (_, e) => errors.Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => errors.Fatal(e.ExceptionObject as Exception);

        host.Start(); // backs up and migrates the database before the window opens

        // WinForms owns this thread until the window closes; then the host is disposed.
        Application.Run(host.Services.GetRequiredService<MainForm>());
    }

    // OBS-3: Rewind's own Information and up, but only warnings and errors from Microsoft's
    // libraries, because EF Core logs every SQL command as Information. The host's lifetime
    // lines stay, so each start and Shut Down is easy to find in the file.
    private static void ConfigureLogging(LoggerConfiguration logging, RewindPaths paths) =>
        logging
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .WriteTo.File(
                Path.Combine(paths.Logs, LogFile),
                outputTemplate: LogLine,
                formatProvider: CultureInfo.InvariantCulture, // the same file on any PC (CA1305)
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: LogFilesKept);
}
