# rewind

Windows 98–style register for our family’s home video store: .NET 10, Blazor in WinForms, SQLite, STM32 counter hardware.

## Run it

Rewind runs on Windows 10 or 11 with the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and the WebView2 Runtime, which Windows 11 already has.

Its five sound files aren't in Git (GIT-6), so on a new PC, first copy them into `src/Rewind.Register/Sounds`; [its README](src/Rewind.Register/Sounds/README.md) lists them. Without them Rewind still runs, silently, and logs a warning for each missing sound.

In PowerShell, from the repo root:

```
$env:Paths__Root = 'C:\RewindDev'
dotnet run --project src/Rewind.Register
```

That's a Debug build, made for trying Rewind without the counter hardware: the scan simulator sits above the status bar (see [Barcode scanner](#barcode-scanner)), and the Store Manager screen (F5) has a Diagnostics panel for trying sounds, dialogs and crashes. Intake (F4) also needs TMDB's token; see [TMDB](#tmdb).

Rewind keeps its database, backups and logs under one folder: `C:\Rewind`, or the full path in the `Paths__Root` environment variable. `C:\Rewind` is the register's own folder, with the store's real data, so a Debug build refuses it: without `Paths__Root`, it says "Rewind can't start" and closes. Set `Paths__Root` in the same window as `dotnet run`. Visual Studio's Run button sees it only if Visual Studio was started from that window, such as with `devenv Rewind.slnx` in a Developer PowerShell.

Until the mini PC arrives, the store itself runs from `main` on the development PC, in a window where `Paths__Root` isn't set. Installing tagged releases, as [Release to the register](#release-to-the-register) describes, starts with the mini PC.

```
dotnet run -c Release --project src/Rewind.Register
```

`$env:Paths__Root` lasts as long as its PowerShell window. A Release build started from a window that set it would run movie night on the test folder, with nothing to stop it, so first run `Remove-Item Env:Paths__Root`, or use another window.

## Barcode scanner

Rewind reads a USB barcode scanner in serial (COM) mode and finds it by its USB IDs, never by its COM number (HW-2). The IDs live in `src/Rewind.Register/appsettings.json`. For example, for the TEEMI TMSL-77:

```json
"Scanner": { "VendorId": "2DD6", "ProductId": "212A", "BaudRate": 115200 }
```

Device Manager shows them in the scanner's Properties, on the Details tab under Hardware Ids, as `USB\VID_2DD6&PID_212A`. A scanner with its own USB serial port ignores the baud rate; a USB-to-serial bridge passes it on.

The status bar shows the last scan, with the member's name when it's someone's member card, and whether the scanner is ready, missing or busy. Busy usually means another program, such as PuTTY, has the port open.

In Debug builds, the row above the status bar is the scan simulator (TEST-5): type what a barcode holds, such as `RV-00042`, and press Enter. It arrives just like a real scan.

### The TEEMI TMSL-77

The scanner keeps its own settings, so a new PC needs only the driver.

- **Driver:** TEEMI's Virtual COM driver, from [teemistore.com/download](https://www.teemistore.com/download/) under "Activate USB virtual COM Port Feature on 2D Wired Scanners". Keep a copy of the installer with the backups, in case the link disappears.
- **Settings,** for a new scanner or after a factory reset: scan that page's Virtual COM barcode, then these from the printed manual: **Add CR** (`090200.`), **Good Read Beeper OFF** (`0502100.`), so only Rewind beeps, and **Enable UPC-E0 Expand** (`0212031.`). Expansion is off from the factory, and without it a UPC-E, the 8-digit short form of a UPC-A, arrives as 8 digits, which Rewind treats as unknown.
- **Factory reset:** **Restore All Factory Defaults** (`0D0100.`) puts it back in keyboard mode with no suffix.
- **Never scan** "remove the initial zero" (`0211030.`): it shortens a UPC-A to 11 digits, which Rewind treats as unknown.

## Intake

Intake (F4) turns a DVD into a labeled case on the shelf: a scan of the DVD's factory barcode asks UPCitemdb what the product is called, that title is searched for on TMDB, where the clerk picks the movie, or each movie of a box set, and a scan of the next store label puts the case on the shelf. Nothing is saved until that label scan ([ADR-0005](docs/adr/0005-intake-saves-at-the-label-scan.md)).

### UPCitemdb

Rewind looks barcodes up on [UPCitemdb](https://www.upcitemdb.com/)'s free plan, which needs no signup and no key. It allows 100 lookups a day and 6 a minute, counted by internet address, so another computer on the store's Wi-Fi spends the same 100. Rewind saves every answer in the database's `UpcLookups` table, so a barcode it has looked up before costs no other lookup. "Not found" is saved only when UPCitemdb itself says so, and only for 30 days, since UPCitemdb keeps growing.

Lookups go one at a time, at most 6 in any minute: a quick run of scans goes straight through, and a seventh waits its turn while the screen says for how long. Esc stops a lookup, and the clerk types the title instead; with nothing to stop or close, Esc starts over, so a DVD scanned by mistake doesn't lend its barcode to the next title typed. When UPCitemdb says Rewind is asking too fast, Rewind asks nothing for a minute; when the day's lookups are used up, nothing until UPCitemdb's Retry-After says they're back, and Intake says when that is. When UPCitemdb is busy, offline or out of lookups, Intake asks the clerk to type the title. That answer isn't saved, so the next scan of the barcode asks again.

Before searching TMDB, Rewind drops words such as DVD, Widescreen and Special Edition from the product's title, and anything in brackets, so "Tommy Boy (DVD)" becomes "Tommy Boy", and it puts a product list's "Matrix, The" back as "The Matrix". It cleans each title as it reads it from `UpcLookups`, so a better word list in `ProductTitles` cleans the answers already saved too.

To try it in a Debug build, open Intake (F4), type a 12-digit UPC such as `032429258373` into the scan simulator and press Enter: the title box fills in with Tommy Boy, and the list opens. Press Enter on Tommy Boy, then type the store label `RV-00042` into the scan simulator and press Enter, and the case goes on the shelf. Each factory barcode Rewind hasn't seen before spends one of the day's 100 lookups.

With the log at Debug (see [Logs](#logs)), each lookup has a line with the barcode, what UPCitemdb answered and how many lookups it says are left. A barcode answered from `UpcLookups` gets a line saying so, a lookup that waits its turn says for how long, and after a refusal the log says until when Rewind asks nothing.

### TMDB

Intake finds each movie on [TMDB](https://www.themoviedb.org/), using TMDB's **API Read Access Token**: the long one on the API page of the TMDB account's settings, not the 32-character API Key. The token is a secret, so it never goes in Git or in `appsettings.json` (SEC-1). Rewind starts without it, and Intake then says it can't reach TMDB.

- **On a development PC,** keep it in dotnet user-secrets, which Debug builds read:

  ```
  dotnet user-secrets set Tmdb:AccessToken <token> --project src/Rewind.Register
  ```

- **On the register,** keep it in `C:\Rewind\keys\secrets.json`. Only the Counter account should be able to open the `keys` folder.

  ```json
  { "Tmdb": { "AccessToken": "<token>" } }
  ```

Every build reads `keys\secrets.json` under its own root. So a Debug build on `C:\RewindDev` reads `C:\RewindDev\keys\secrets.json`, which is the easy way to give a test folder the token without user-secrets. When both have it, user-secrets win.

Rewind reads the token once, as it starts, so restart it after setting or changing the token. It won't start with a `secrets.json` that isn't valid JSON, or with a token that has a space in it, such as `Bearer` pasted in front: a message box says why. When TMDB refuses the token, Intake says the token isn't set up, and the log says TMDB refused it.

Rewind saves each movie's poster in `C:\Rewind\data`, named for the movie's TMDB ID: `posters\948.jpg` at 342 pixels wide for the register's screens, and `thumbs\948.jpg` at 154 for the phone catalog. Posters aren't in Git (GIT-6) or in the backups, which hold only the database.

## Screen size

Rewind is laid out for 1024 × 768 (UI-8). On a bigger monitor, set the zoom in `src/Rewind.Register/appsettings.json` to a whole number, such as 2 to draw everything twice as large. Whole numbers keep the pixel fonts sharp.

```json
"Display": { "Zoom": 2 }
```

## Logs

Rewind writes one log file a day to `C:\Rewind\logs`, such as `rewind-20261023.log`, and deletes each one 30 days later (OBS-2). It logs at Information: starts, Shut Downs, devices coming and going, and every error with its details.

To troubleshoot, set the level to Debug in `appsettings.json`, which on the register sits next to `Rewind.Register.exe`. Debug adds detail such as every scan and every sound that fails to play. Set it back to Information afterwards: the next release's `appsettings.json` replaces the register's anyway.

```json
"Log": { "MinimumLevel": "Debug" }
```

The other levels are Information, Warning and Error. Rewind won't start with one it doesn't know: a message box says why, then Rewind closes. That reason never reaches the log, because the log needs the setting to start.

## Test

```
dotnet test
dotnet format --verify-no-changes
```

`dotnet test` runs the Core tests (xUnit, over real SQLite files in a temporary folder) and the screen tests (bUnit). CI runs both commands on every push, and also builds and runs the firmware parser's C tests with gcc and checks `keyboard.js` with Node; [ci.yml](.github/workflows/ci.yml) has those commands.

To check the label and card sheets without a printer:

1. Run Rewind with a scratch `Paths__Root`, press F5 and click **Print 30 labels**. In the print window, pick **Save as PDF**, with Scale 100, headers and footers off and the margins as they are, and save the PDF outside the repo. Microsoft Print to PDF doesn't work from Rewind's print window: it says "Print failed", or prints nothing.
2. Open it at 100%: it's one Letter page, with three columns of ten barcodes, centered 1.5, 4.25 and 7 in from the left edge, in rows 1 in apart, the first about 1 in from the top.
3. Scan a barcode off the screen, zoomed in if it won't read: the status bar says `RV-00001 · Store label`. **Print 10 member cards** should give `MEM-001 · Member card`, with cut lines 0.75 in from the left and right edges and 0.5 in from the top and bottom, and a Name line above each barcode. A card someone already has shows their name instead.
4. Do it again with `$env:Display__Zoom = '2'` set first: the PDF should come out the same size, since printing ignores the screen's zoom.

The first time a printer prints labels, print a sheet on plain paper and hold it over a label sheet against a window, to check each barcode sits inside its label. Then print one real sheet and scan a label before printing more: the status bar should say **Store label**, and **Member card** for the first card printed.

## Change the database

The schema changes only through EF Core migrations (DATA-5). The tools install once per PC with `dotnet tool install --global dotnet-ef`. After changing an entity or its configuration, add a migration from the repo root, then print the SQL it will run:

```
dotnet ef migrations add AddCases -p src/Rewind.Core -s src/Rewind.Register
dotnet ef migrations script Initial AddCases -p src/Rewind.Core -s src/Rewind.Register
```

The script covers the migrations after the first name, up to the second, so use the migration before yours in place of `Initial`. Read the script before you commit. SQLite can't change most columns, or any CHECK, in place, so EF Core rebuilds the table: it copies every row into an `ef_temp_` table, drops the old one and renames the copy. Check that the copy carries every column across: EF Core can read a renamed property as one column dropped and another added, which empties it.

EF Core builds the new table from the migration's `.Designer.cs` file, the model as it was when the migration was added, and not from `Up()`. So when the script shows a wrong rule, fix the model, then remove the migration and add it again; never edit `Up()`. A rebuild also runs as at least three transactions, because SQLite can't turn foreign keys off inside one, so the start that applies it logs a warning from EF Core, that an operation "cannot be executed in a transaction". That's expected: for HoldRatingToUsRatings it was one line, about its `PRAGMA foreign_keys = 0`.

The tools work on a scratch `design.db` and never touch `C:\Rewind`. Rewind applies new migrations itself when it starts, after a backup. Until a migration has run on the register, `dotnet ef migrations remove -p src/Rewind.Core -s src/Rewind.Register` takes it back out. Once it has run there, never edit it; add another migration instead.

### If a migration was interrupted

EF Core holds a lock while it migrates: a row in the `__EFMigrationsLock` table. If Rewind closes in the middle of a migration, after a crash or a power cut, SQLite rolls back the transaction it was in. For most migrations that's the whole migration, but a table rebuild runs as at least three, so it can stop partway (step 5). Either way the row stays. The next start then waits a minute and fails: a message box says the migration lock was still held, and so does the log in `C:\Rewind\logs`. Each start after that makes another backup and fails the same way, until the lock is cleared:

1. Make sure no copy of Rewind is running: Task Manager lists none named `Rewind.Register.exe`. A copy that's still migrating holds the lock for real, and clearing it would let two copies migrate at once.
2. Open the database in the SQLite shell: `sqlite3.exe`, from the sqlite-tools zip for Windows on [sqlite.org/download.html](https://sqlite.org/download.html), with no installer needed.

   ```
   sqlite3 C:\Rewind\data\rewind.db
   ```

3. Clear the lock, then leave the shell:

   ```
   DELETE FROM "__EFMigrationsLock";
   .quit
   ```

4. Start Rewind. It backs up the database again and migrates it.
5. If a start fails with `table "ef_temp_Movies" already exists`, or names another `ef_temp_` table, a rebuild stopped between its first and second transactions. That can be the first failure you see, with no lock to clear: when the second transaction fails without a crash, such as on a full disk, SQLite rolls it back and EF Core releases the lock, but the first transaction's `ef_temp_` table stays. The original table is untouched, because the step that replaces it is the one that didn't run. In the SQLite shell, as in step 2, drop the leftover table, with the name from the message:

   ```
   DROP TABLE "ef_temp_Movies";
   .quit
   ```

   Then start Rewind again.

## Change the docs

The Coding Standards, the Build Plan and the Project Overview live in `docs/` as Word files, and change through a PR like the code. Move the date under a document's title to the day you change it: that date is how the review project's copies are told apart.

Git shows a Word file only as "Binary files differ". To see the text that changed instead, run this once in each clone, in Git Bash:

```
git config diff.docx.textconv "sh \"$(git rev-parse --show-toplevel)/tools/docx-text.sh\""
```

The clerk handbook, `docs/handbook/handbook.html`, is a web page whose own print styles set how big it prints (DOC-6): both sides of one sheet, with movie night on the front. Open it in Edge or Chrome to see each side as a sheet of paper. Text that runs past a sheet's bottom edge won't fit when it's printed. The page says how to print it.

## Release to the register

The register only ever runs a tagged build (GIT-7).

1. Run the movie-night checklist on the real counter (TEST-9).
2. On an up-to-date `main` that CI has passed, tag the release and push the tag, for example `git tag v0.1.0` and `git push origin v0.1.0`.
3. Copy the five sound files into `src/Rewind.Register/Sounds`. They aren't in Git; see [its README](src/Rewind.Register/Sounds/README.md).
4. Build Rewind and its .NET runtime into one folder:

   ```
   dotnet publish src/Rewind.Register -c Release -r win-x64 --self-contained
   ```

5. Zip that folder and attach the zip to the tag's GitHub release. In PowerShell:

   ```
   Compress-Archive src\Rewind.Register\bin\Release\net10.0-windows\win-x64\publish\* rewind-v0.1.0.zip
   gh release create v0.1.0 rewind-v0.1.0.zip
   ```

6. On the register, shut Rewind down (F5, then Shut Down, then OK). Rename its program folder, the one Task Scheduler starts `Rewind.Register.exe` from, to keep the old version, unzip the release into a new folder with the original name, and start Rewind. If the release adds a migration, Rewind backs up the database before applying it.

The zip includes `appsettings.json`, so a setting changed by hand on the register is lost unless the repo has it too.

## Restore a backup

Before Rewind applies a migration, it backs up the database to `C:\Rewind\backups`, in a file named for the local time, such as `rewind-2026-10-23-2215.db`. To go back to one:

1. Shut Rewind down (F5, then Shut Down, then OK). Never copy the database while Rewind runs (DATA-14).
2. In `C:\Rewind\data`, move `rewind.db` into a new folder, such as `C:\Rewind\data\before-restore`, along with `rewind.db-wal` and `rewind.db-shm` if they're there. Keep the three together: the `-wal` file can hold changes that aren't in `rewind.db` yet, and SQLite would apply a `-wal` left behind to the restored database and corrupt it.
3. Copy the backup to `C:\Rewind\data\rewind.db`.
4. Start Rewind. If the backup is from an older release, Rewind backs it up again and migrates it.

Anything entered after the backup was made isn't in it, but the moved files still have it. An older release may not work with a database that a newer one has migrated, so going back a release also means restoring the backup made just before that migration.
