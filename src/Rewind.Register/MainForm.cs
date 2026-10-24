using System.ComponentModel;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.Hosting;
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
    private const string ShellSelector = "#app";

    private bool _stopping;

    public MainForm(IServiceProvider services, IHostApplicationLifetime lifetime)
    {
        Text = Store.Name;
        FormBorderStyle = FormBorderStyle.None; // borderless before maximized,
        WindowState = FormWindowState.Maximized; // so the window covers the taskbar
        BackColor = Color.Teal; // a Windows 98 desktop while the web view loads

        var web = new BlazorWebView
        {
            Dock = DockStyle.Fill,
            HostPage = HostPagePath,
            Services = services, // BlazorWebView needs the container itself
        };
        web.RootComponents.Add<Shell>(ShellSelector);
        web.BlazorWebViewInitialized += (_, e) => LockDown(e.WebView);
        web.UrlLoading += (_, e) => StayInsideRewind(e);
        Controls.Add(web);

        // Shut Down in the Store Manager stops the host; only then may the window close.
        lifetime.ApplicationStopping.Register(() => BeginInvoke(Stop));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Alt+F4 and closing from the taskbar both arrive as UserClosing.
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
#if !DEBUG
        settings.AreDevToolsEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false; // F5, Ctrl+R, Ctrl+P, Ctrl+F, F12
#endif
    }

    // Links outside the app are cancelled instead of opening the default browser.
    // New-window requests (a middle-click or Ctrl+click on a link) skip this event
    // and always open the browser, so Rewind's pages use no href links.
    private static void StayInsideRewind(UrlLoadingEventArgs e)
    {
        if (e.UrlLoadingStrategy != UrlLoadingStrategy.OpenInWebView)
        {
            e.UrlLoadingStrategy = UrlLoadingStrategy.CancelLoad;
        }
    }
}
