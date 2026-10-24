# rewind

Windows 98–style register for our family’s home video store: .NET 10, Blazor in WinForms, SQLite, STM32 counter hardware.

## Barcode scanner

Rewind reads a USB barcode scanner in serial (COM) mode and finds it by its USB IDs, never by its COM number (HW-2). The IDs live in `src/Rewind.Register/appsettings.json`. For example, for a Nucleo-64's ST-LINK:

```json
"Scanner": { "VendorId": "2DD6", "ProductId": "212A", "BaudRate": 115200 }
```

Device Manager shows them in the scanner's Properties, on the Details tab under Hardware Ids, as `USB\VID_0483&PID_374B`. A scanner with its own USB serial port ignores the baud rate; a USB-to-serial bridge passes it on.

The status bar shows the last scan and whether the scanner is ready, missing or busy. Busy usually means another program, such as PuTTY, has the port open.

In Debug builds, the row above the status bar is the scan simulator (TEST-5): type what a barcode holds, such as `RV-00042`, and press Enter. It arrives just like a real scan.

    ### The TEEMI TMSL-77

    The scanner keeps its own settings, so a new PC needs only the driver.

    - **Driver:** TEEMI's Virtual COM driver, from [teemistore.com/download](https://www.teemistore.com/download/) under "Activate USB virtual COM Port Feature on 2D Wired Scanners". Keep a copy of the installer with the backups, in case the link disappears.
    - **Settings,** for a new scanner or after a factory reset: scan that page's Virtual COM barcode, then these from the printed manual: **Add CR** (`090200.`) and **Good Read Beeper OFF** (`0502100.`), so only Rewind beeps.
    - **Factory reset:** **Restore All Factory Defaults** (`0D0100.`) puts it back in keyboard mode with no suffix.
    - **Never scan** "remove the initial zero" (`0211030.`): it shortens a UPC-A to 11 digits, which Rewind treats as unknown.

## Change the database

The schema changes only through EF Core migrations (DATA-5). The tools install once per PC with `dotnet tool install --global dotnet-ef`. After changing an entity or its configuration, add a migration from the repo root, then print the SQL it will run:

```
dotnet ef migrations add AddCases -p src/Rewind.Core -s src/Rewind.Register
dotnet ef migrations script Initial AddCases -p src/Rewind.Core -s src/Rewind.Register
```

The script covers the migrations after the first name, up to the second, so use the migration before yours in place of `Initial`. Read the script before you commit. SQLite can't change most columns in place, so EF Core rebuilds the table: it copies every row into an `ef_temp_` table, drops the old one and renames the copy. Check that the copy carries every column across: EF Core can read a renamed property as one column dropped and another added, which empties it.

The tools work on a scratch `design.db` and never touch `C:\Rewind`. Rewind applies new migrations itself when it starts, after a backup. Until a migration has run on the register, `dotnet ef migrations remove -p src/Rewind.Core -s src/Rewind.Register` takes it back out. Once it has run there, never edit it; add another migration instead.
