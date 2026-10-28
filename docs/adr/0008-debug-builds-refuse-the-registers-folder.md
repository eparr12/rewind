# ADR-0008: Debug builds refuse the register's folder

- **Status:** Accepted
- **Date:** 2026-10-28
- **Author:** Ethan

## Context

Rewind keeps its database, backups, logs and secrets under one root folder: `C:\Rewind`, unless Paths:Root names another (NET-11). Until the mini PC arrives, the development PC is the register, so `C:\Rewind` on it holds the store's real data, including the counters that number its labels and cards (ADR-0006).

A Debug build used the same default. On 2026-10-24, a test run set Paths__Root in Visual Studio's Developer PowerShell, then used Visual Studio's Run button. That window is Visual Studio's child, so the variable never reached Rewind, and three test movies and six posters went into the real database. Nothing in Rewind deletes a movie (DATA-4), so they had to come out by hand, after a backup.

## Decision

A Debug build won't start with Paths:Root at the register's folder.

- **The check.** Program.cs adds a Debug-only rule to the Paths options, beside AddRewindCore's full-path rule. It asks `RewindPaths.IsRegisterRoot`, which matches `C:\Rewind` in any capitals, with either slash, and with slashes doubled or at the end.
- **Where it fails.** The logger reads the paths while the host is built, so a refused start fails there. The plain "Rewind can't start" box says why and what to set, and Rewind exits with code 2 (OBS-5). Nothing at all is written under the root, not even a log line.
- **Release builds don't have it,** so the register's own runs are unchanged.
- **The README's run steps** set Paths__Root first, to a test folder such as `C:\RewindDev`.

## Alternatives

- **Default Debug builds to another folder, such as `C:\RewindDev`.** There'd be nothing to set, and Visual Studio's Run button would just work. But a test would quietly use whatever that folder held, the same setting would mean a different folder in Debug and Release, and Paths__Root set to `C:\Rewind` would still reach the real data.
- **A launch profile that sets Paths__Root.** Visual Studio's Run button reads `launchSettings.json`, but so does `dotnet run`, and until the mini PC arrives the register runs `dotnet run -c Release` from source. The register would start on the test folder.
- **Paths__Root as a user-level environment variable,** the one most people would reach for: Visual Studio and every new window would inherit it from the Start menu. But until Build Plan 6.1 sets up the mini PC with its own Counter account, the register runs under this same Windows account, so it would start on the test folder too, as with a launch profile.
- **Leave it to the test plans.** They already said to set Paths__Root and use `dotnet run`, and one missed step was enough.

## Consequences

- **Every Debug run needs Paths__Root,** set in the window that runs `dotnet run`. Visual Studio's Run button is refused unless Visual Studio was started from a window that has it set.
- **That Developer PowerShell step is temporary.** Once the mini PC is the register, nothing real runs under the development PC's account, and a user-level Paths__Root there becomes the right answer: Visual Studio's Run button and every window get it.
- **It stops mistakes, not someone set on getting past it.** It catches the default and the ways a person would type `C:\Rewind`. It doesn't follow links, resolve `.` and `..` folders, or know other spellings Windows accepts, such as `\\?\C:\Rewind`.
- **Tests and tools are unaffected.** Tests use their own temporary roots and never build the host. With RewindDbFactory, `dotnet ef` never runs Rewind's Main, so it never meets the check.
- **It holds on every PC,** not just the register: `C:\Rewind` is the register's folder wherever Rewind runs, and a development PC can be the register, as this one is.
