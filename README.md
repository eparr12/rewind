# rewind

Windows 98–style register for our family’s home video store: .NET 10, Blazor in WinForms, SQLite, STM32 counter hardware.

## Barcode scanner

Rewind reads a USB barcode scanner in serial (COM) mode and finds it by its USB IDs, never by its COM number (HW-2). The IDs live in `src/Rewind.Register/appsettings.json`. For example, for a Nucleo-64's ST-LINK:

```json
"Scanner": { "VendorId": "0483", "ProductId": "374B", "BaudRate": 115200 }
```

Device Manager shows them in the scanner's Properties, on the Details tab under Hardware Ids, as `USB\VID_0483&PID_374B`. A scanner with its own USB serial port ignores the baud rate; a USB-to-serial bridge passes it on.

The status bar shows the last scan and whether the scanner is ready, missing or busy. Busy usually means another program, such as PuTTY, has the port open.

In Debug builds, the row above the status bar is the scan simulator (TEST-5): type what a barcode holds, such as `RV-00042`, and press Enter. It arrives just like a real scan.
