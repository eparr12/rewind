# ADR-0006: Label numbers are spent at print time

- **Status:** Accepted
- **Date:** 2026-10-25
- **Author:** Ethan

## Context

Every case gets a removable store label, RV-00001 to RV-99999, and every family member gets a membership card, MEM-001 to MEM-999 (Overview: Barcodes and intake). Build Plan 2.5 prints them in sheets from the Store Manager:

- 30 store labels on an Avery 6460 sheet;
- 10 member cards on card stock, 2 across and 5 down.

It asks for the next label number to be kept in StoreSettings, "so batches never repeat a number."

A repeated number is the one thing that mustn't happen. Cases.StoreLabel is unique (DATA-11), so Intake would refuse the second case a repeated label went on. But the clerk would only find out at the counter, with the label already stuck on the case.

Rewind can't know whether a sheet printed:

- WebView2's print window doesn't say whether the clerk printed or cancelled.
- A printer can jam or run out of toner, and a sheet can be thrown away.
- A sheet is printed long before its last label goes on a case at intake, so Cases knows only the labels that are on cases.

## Decision

A sheet's numbers are spent the moment the sheet is made, before the print window opens.

- **Two counters.** StoreSettings keeps NextStoreLabelNumber and NextMemberCardNumber. They start at 1: the migration that adds them gives them DEFAULT 1, so the register's existing row starts at RV-00001 and MEM-001 too.
- **One transaction.** Making a sheet reads the counter, draws the sheet and saves the counter advanced past it, all in one transaction (DATA-7).
  - EF Core begins it IMMEDIATE, Microsoft.Data.Sqlite's default, which takes SQLite's write lock before the read. So a second sheet made at the same moment waits until the first has committed, then reads the advanced counter.
  - That transaction is the guard behind "never", the way Cases.StoreLabel's unique index is the guard behind refusing a label that's in use (DATA-11, ADR-0005).
  - The Store Manager's busy flag, which stops a double click from making two sheets, is only manners.
- **Full sheets only:** 30 labels or 10 cards. A sheet that would pass RV-99999 or MEM-999 is refused, and nothing is spent. So the last nine numbers of each, RV-99991 to RV-99999 and MEM-991 to MEM-999, are never printed.
- **"Print this sheet again"** prints the same numbers while the sheet is still on screen, for a sheet that jammed or didn't print.

## Alternatives

- **Take the next number from max(Cases.StoreLabel) + 1.** Wrong: labels that are printed but not on a case yet aren't in Cases, so the next sheet would repeat them.
- **Ask "Did it print?" after the print window closes, and spend the numbers only on Yes.** A wrong No, or the clerk leaving the screen without answering, would print the same numbers on the next sheet.
- **Let the clerk type the number a sheet starts at.** One mistyped digit repeats a whole sheet.
- **Start a sheet at label N, to finish a partly used sheet.** Rewind builds only what the phase needs (GEN-5), and partly used label sheets jam laser printers.
- **Read the counter and save it in one SaveChanges, with no transaction around the read.** Two sheets made at the same moment could both read the same counter. The busy flag prevents that from one screen, but only the transaction prevents it in the data.

## Consequences

- **Skipped numbers.** A cancelled print, a jam, or leaving the screen before printing skips that sheet's numbers: 30 labels or 10 cards. Nothing needs the numbers to be consecutive, so a gap is harmless. The log has each sheet's first and last code.
- **Repeated labels.** No two sheets share a number, but one sheet can still print twice:
  - after "Print this sheet again" on a sheet that did print;
  - with more than one copy chosen in the print window.

  Intake refuses the second case such a label goes on, and the clerk peels the next one (ADR-0005). The clerk handbook will say to reprint only a sheet that didn't print, and to print one copy, when the screen arrives in the pull request that follows.
- **No CHECK on the counters (GEN-5).** Only an edit by hand can move one, and Cases' CHECK and Barcodes still hold labels to their format.
- **How long the numbers last.** At 30 labels a sheet, 99,999 numbers last 3,333 sheets.
- **The Store Manager screen.** It shows the next codes, makes a sheet in one call, and opens the print window only once the sheet is drawn on screen. Leaving the screen drops the sheet, and the handbook will say so.
