# ADR-0013: Close Out needs no open night, and its files are its record

- **Status:** Accepted
- **Date:** 2026-11-16
- **Author:** Ethan

## Context

Build Plan 3.5 has the Close Out Register button close the day, back up, write the catalog and copy to USB, and says none of its steps can fail the Close Out. Its Done when asks two Close Outs in one night for two backups, and a missing USB drive for a warning instead of an error. The Overview's "Close Out and backups" says Close Out always finishes, that a copy that can't happen is reported on screen and tried again at the next Close Out, and that the register keeps its newest 30 backups while the USB drive keeps every one. The R4 sheet's sketch closes the night, then runs each other step in a `Try` that warns and goes on, on any error but a cancel.

That leaves three questions.

- **A night to close.** A night opens with the first member card scanned at Checkout, and a return opens none (the Overview's "Checkout, returns and late fees"). So a night of returns only has no night open at Close Out, and neither does a night of intake only, or a second Close Out the same night. The sketch's first step would have nothing to close.
- **Where a backup and a copy are written down.** The Overview's data model gives BusinessDays "backup and copy times", but a Close Out with no night open has no row to put them on. The status bar shows the last backup (OBS-6), so something has to say when that was.
- **What's on disk when something goes wrong.** A backup, a copy on the drive, the catalog and a thumbnail can each be cut short, by a crash, a power cut, a full drive or a drive pulled out. A file cut short looks whole to anything that only checks it's there.

The backup has been `Backups.CreateAsync` since #3, which made one before every migration. PRs 1 and 2 of 3.5 built the other steps: `PhoneCatalog.WriteAsync`, `BackupDrive.CopyAsync`, and `Backups.List` and `KeepNewest`, with `WholeFile` for the writes. PR 2 also had `CreateAsync` make each backup under another name first, as `WholeFile` does, and settled the rules for the copy to the drive, which this ADR records with the rest.

## Decision

Close Out needs no open night, and its files are its record.

- **Close Out runs with or without an open night.** A night opens with the first member card at Checkout, so a night of returns only, a night of intake only and a second Close Out the same night all have none. Closing then answers "nothing open", and the backup, the catalog and the copy still run. A return's watch log needs backing up as much as a rental does, and Build Plan 3.5's Done when asks a second Close Out for a second backup.
- **Only closing the night can fail a Close Out.** When closing throws, so does Close Out, and nothing else has happened. Each step after it is tried and reported, and logged as a Warning when it didn't work. None stops the next (GEN-7).
- **Once the night is closed, nothing cancels the rest** (NET-7). A cancel before then, such as the clerk leaving the screen, stops Close Out with nothing done. From then on the steps don't hear it, and the checklist's report comes back.
- **The files are the record.** The database gets no backup or copy times. The newest file in the backups folder says when the last backup was, and the drive's folder says what the drive holds.
- **Every file is whole or not there.** A backup, a copy on the drive, the catalog and a thumbnail are each written under a name ending in `.partial`, and renamed once they're whole. A write or a backup that fails deletes its own unfinished file. One that a crash or a power cut leaves stays. In the register's backups folder it's never listed, counted, copied or written over. In the outbox the next write of that file replaces it, and on the drive the next copy of that backup does.
- **The USB step copies whatever the drive is missing,** into a folder named for the root folder's name, and never to the register's own disk. A drive left at home gets the nights it missed the next time it's plugged in, and a copy on the register's own disk isn't a second copy. PR 2 settled the rest:
  - **The newest backup goes first,** so a drive that fills up or is pulled out partway has tonight's.
  - **The first write a drive fails ends that drive's copy for the night,** since a drive that's full, read-only or gone fails every write the same way. Any other backup drive still gets its backups.
  - **A drive has a backup when it has a file of that name and length.** A file of that name and another length is replaced, so the register's copy is the one that counts.
  - **The register's own disk is the drive the root folder is on.** A second partition of the same disk counts as another drive: telling them apart takes WMI, which Core can't use (ARCH-1).
  - **A backup the register itself can't read fails the step,** rather than being answered as the drive's fault, so the checklist never blames a good drive.
- **The register keeps its newest 30 backups, counted after the copy,** and the drive keeps every one. So a backup on its way out reaches the drive first.

## Alternatives

- **Backup and copy times on BusinessDays,** which the Overview's data-model row promises today. A Close Out with no night open has nowhere to put them. And they'd be a second fact to keep in step with the files, which ADR-0009, ADR-0010 and ADR-0012 each turned down: a backup deleted by hand, or a drive wiped or swapped for a new one, would leave a time that says what isn't so, and the status bar would show a backup the folder doesn't have.
- **A CloseOuts table with a row for each run.** It gives a Close Out with no night somewhere to go, and keeps a history the checklist could read. But it's the same second fact with more rows, and nothing in Phase 3 reads it (GEN-5). No backup could hold the row that says it was made, since the row is written after it, and the row is one more write that can fail once the night is closed.
- **Refusing to close out with no night open.** "Close Out closes the night" would stay true without exception. But a night of returns only would leave the watch log it filled in without a backup, and that's the heirloom. Build Plan 3.5's Done when asks a second Close Out the same night for a second backup, too.
- **Opening a night for a return.** Every Close Out after a return would have a night to close. But in the Overview a return opens no night and belongs to none. Cases come back at any time, such as the next morning, so a night a return opened would often stay open into the next movie night, and that night's draws and rentals would land on it, as #59 describes for a skipped Close Out. A night of intake only would still have none.
- **Copying only tonight's backup,** as the sketch's `CopyNewestAsync` names it. It's one file a night. But a drive left at home on movie night would never get that night's backup, so the drive would have gaps that only a copy by hand could fill. Copying whatever it's missing costs nothing more on a night when it has the rest.
- **A saved queue of copies to retry.** It would remember which copies to try again at the next Close Out. But the two folders already say: the register's backups that the drive doesn't have are the queue, worked out each night, with nothing to keep in step and nothing a hand edit can leave wrong.
- **Telling a drive's copy by its bytes rather than by its name and length.** It would catch a copy changed after it was written. But it reads every backup on the drive back at every Close Out, and the drive keeps every one, so that read grows every week. Rewind's own copies are whole or not there, and a name and length still catch a copy cut short, such as one made by hand onto a full drive.
- **Sweeping up the unfinished files a crash left.** The folders would stay tidy. But Close Out would then delete files that aren't backups, in folders where Rewind deletes nothing but its own old backups, and the backups folder holds the scanner driver's installer too. An unfinished backup is never listed, counted or copied, so it does no harm, and the README's "Restore a backup" says it's safe to delete. In the outbox and on the drive, the next write of that file replaces it anyway.

## Consequences

- **The Overview's BusinessDays row loses "with backup and copy times"** in PR 7.
- **Build Plan 7.2 decides where the last NAS copy's time is kept,** when it needs one. The checklist shows the date of the last NAS copy (the Overview's "Close Out and backups").
- **The 30 are backups, not nights.** A second Close Out the same night, or the backup Rewind makes before a migration, takes one of the 30 too. The drive still has every one.
- **A drive that's away for more than 30 backups misses the oldest.** The register has deleted them by the time it's back.
- **The drive is never pruned, so one day it fills up.** From then on the USB step answers that the drive couldn't be written to, until the Store Manager makes room or marks a new drive.
- **Two root folders with the same name share one folder on the drive.** A test folder named `Rewind` anywhere but `C:\Rewind` would put its backups among the store's, and one made in the same minute as a store backup, with another length, would replace the store's copy there until the store's next Close Out. The README's test folder is `C:\RewindDev`, and PR 6's README will say never to name one `Rewind`.
- **A backup changed on the register after it was copied,** such as one opened in a tool that writes to it, replaces the drive's copy at the next Close Out when its length has changed. One that kept its length isn't copied again, so the drive keeps the copy from before the change.
- **An unfinished copy stays on the drive when the register no longer has that backup.**
- **#59, the night nobody closed, is still open:** a stale night is closed by the next Close Out, whenever that is.
