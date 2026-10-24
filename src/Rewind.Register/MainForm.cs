using System.ComponentModel;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Web.WebView2.WinForms;
using Rewind.UI;

namespace Rewind.Register;

/// <summary>
/// The register's only window: borderless, full screen, and showing the Blazor shell.
/// </summary>
[DesignerCategory("Code")] // Visual Studio opens this file as code, not in the WinForms designer
internal sealed class MainForm : Form
{
    private const string HostPagePath = @"wwwroot\index.html";
    private const string AppSelector = "#app";

    // A Windows 98 desktop shows until the shell draws over it: first the form's background,
    // then WebView2's, which would otherwise flash white before the page loads.
    private static readonly Color Desktop = Color.Teal;

    private bool _stopping;

    public MainForm(IServiceProvider services, IHostApplicationLifetime lifetime, IOptions<DisplayOptions> display)
    {
        Text = Store.Name;
        FormBorderStyle = FormBorderStyle.None; // borderless before maximized,
        WindowState = FormWindowState.Maximized; // so the window covers the taskbar
        BackColor = Desktop;

        var web = new BlazorWebView
        {
            Dock = DockStyle.Fill,
            HostPage = HostPagePath,
            Services = services, // BlazorWebView needs the container itself
        };
        web.WebView.DefaultBackgroundColor = Desktop;
        web.WebView.ZoomFactor = display.Value.Zoom; // UI-8
        web.RootComponents.Add<App>(AppSelector); // the shell, inside the ErrorBoundary of last resort
        web.BlazorWebViewInitialized += (_, e) => LockDown(e.WebView);
        web.UrlLoading += (_, e) => StayInsideRewind(e);
        Controls.Add(web);

        // Shut Down in the Store Manager stops the host. The window closes once every hosted
        // service has stopped, and Program then disposes the host.
        lifetime.ApplicationStopped.Register(() => BeginInvoke(Stop));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Alt+F4, closing from the taskbar and Stop's own Close() all arrive as UserClosing,
        // so only _stopping tells Shut Down apart from the clerk.
        if (e.CloseReason == CloseReason.UserClosing && !_stopping)
        {
            e.Cancel = true;
        }

        base.OnFormClosing(e);
    }

    private void Stop()
    {
        _stopping = true;
        Close();
    }

    // SEC-6: the web view must never act like a browser. BlazorWebView already
    // turns off the right-click menu and link previews; setting them again keeps
    // that true if a future version changes its defaults.
    private static void LockDown(WebView2 view)
    {
        var settings = view.CoreWebView2.Settings;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false; // Ctrl+wheel, Ctrl+plus, Ctrl+minus
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
#if !DEBUG
        settings.AreDevToolsEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false; // F5, Ctrl+R, Ctrl+P, Ctrl+F, F12
#endif
    }

    // Links outside the app are cancelled instead of opening the default browser.
    // New-window requests (a middle-click or Ctrl+click on a link) skip this event
    // and always open the browser, so Rewind's pages use no href links, and
    // keyboard.js ignores clicks on any that slip in.
    private static void StayInsideRewind(UrlLoadingEventArgs e)
    {
        if (e.UrlLoadingStrategy != UrlLoadingStrategy.OpenInWebView)
        {
            e.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
        }
    }
}
