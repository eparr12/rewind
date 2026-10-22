using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rewind.Register;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Task Scheduler starts Rewind in C:\Windows\System32, so settings are
        // read from the app's own folder rather than the current one.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        var services = builder.Services;
        services.AddWindowsFormsBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        services.AddSingleton<MainForm>();

        using var host = builder.Build();
        host.Start();

        // WinForms owns this thread until the window closes; then the host is disposed.
        Application.Run(host.Services.GetRequiredService<MainForm>());
    }
}
