# ADR-0002: Blazor Hybrid in WinForms on a Windows 11 register

- **Status:** Accepted
- **Date:** 2026-10-21
- **Author:** Ethan

## Context

Rewind is the register for our home video store. It runs full screen on a Windows 11 mini PC at the counter, and it has to:

- look like a 1998 Windows program: beveled gray buttons, navy title bars and pixel fonts;
- read a USB barcode scanner and an STM32 board over serial ports;
- play several sounds at once, with no browser autoplay rules;
- work fully offline, with the data in one SQLite file on the register;
- start like an appliance and recover from a crash or a power cut;
- be built quickly by one developer whose daily skills are C#, .NET, Blazor, EF Core and WinForms;
- still build and run in 20 years.

## Decision

Rewind is one .NET 10 program: a borderless WinForms window that hosts Razor components through `BlazorWebView`, styled with 98.css.

- **Rewind.Register** (`net10.0-windows`) is the only Windows-specific project. It holds the form, `BlazorWebView`, the WebView2 lockdown settings, NAudio, device discovery and startup.
- **Rewind.UI** (`net10.0`) is a Razor class library with the screens, dialogs, 98.css and the keyboard module. It references only Rewind.Core.
- **Rewind.Core** (`net10.0`) holds the rules, EF Core with SQLite, the serial device readers and the HTTP clients. It references no UI or Windows packages.

The components run in-process, so they call Core services directly, with no web server and no API between them.

## Alternatives

- **Plain WinForms or WPF with custom drawing.** These give full control, but recreating Windows 98's look by hand means a lot of drawing code. 98.css gives that look for free, and Blazor is faster for me to build screens in.
- **Blazor Server or ASP.NET Core with a browser in kiosk mode.** This is close to Hybrid, but it needs a web server, a browser kept in kiosk mode, and a way into serial ports and sound from outside the browser. It adds moving parts to an appliance.
- **.NET MAUI Blazor Hybrid.** The same Blazor idea, but MAUI's Windows story is heavier and less settled than WinForms, and Rewind only needs Windows.
- **Electron or Tauri.** These need a JavaScript or Rust host and a second language for serial ports and sound, and they would throw away my C# skills.

## Consequences

- One language and one process: screens, rules, database and devices are all C#, and debugging is a single session.
- WebView2 must be present. It ships with Windows 11, and the Evergreen runtime updates itself.
- The web view has to be locked down so it doesn't act like a browser: no dev tools, right-click menu, browser shortcuts, zoom keys, autofill or outside links in Release builds (SEC-6).
- Background events from serial threads must reach components through `InvokeAsync` (UI-11).
- Because only Rewind.Register touches Windows, Core and UI build and test on the Linux CI runner, and the screens could move to a Blazor Server host on Linux later without a rewrite.
- The WebView2 package references its WPF control in every Windows project, which causes an MSB3277 conflict over `WindowsBase` in a WinForms app. Rewind.Register removes that reference in its project file.
