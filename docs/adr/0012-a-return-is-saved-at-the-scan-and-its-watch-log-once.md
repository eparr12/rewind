# ADR-0012: A return is saved at the scan, and its watch log once

- **Status:** Accepted
- **Date:** 2026-11-14
- **Author:** Ethan

## Context

Build Plan 3.4 has a return record two things: "Scanning a case puts it back On shelf and stamps its return time," and "Ask who watched, with stars from 1 to 5 for each person; any of it can be skipped." The Overview's steps for Return, on F2, say the same: the clerk scans each case, it goes back On shelf with its return time, and Rewind asks who watched and each person's stars, and skipping is fine. Neither says when each of the two is saved, or what becomes of a watch log nobody answered.

Checkout and Intake save at the end. Checkout keeps the open sale in its own fields and saves nothing until the cash completes it (ADR-0003), and Intake saves nothing until the label scan (ADR-0005). So Esc, or leaving the screen, loses only what was open, and nothing is left half saved.

A return isn't like that. The case is in the clerk's hands, so it's back whatever the clerk does next. If its save waited for the question of who watched, then Esc, a clerk who left the screen, or a crash would leave it checked out, and the next Checkout would refuse it as not on the shelf, on movie night (GEN-7).

ADR-0011's last consequence left the other half open. A check-in that commits as the clerk leaves Return puts the case on the shelf without asking who watched, so can that rental's watch log be filled in later? #79 left room for either answer: a rental item's rows in WatchLog are who the clerk said watched it, so an item with none was skipped or never asked about, and nothing says which.

## Decision

A return is saved at the scan, and its watch log once.

- **The scan checks the case in at once, with no question first.** The rental item's return time and the case's On shelf go in one transaction (DATA-7).
- **Who watched is a second save.** All of one rental item's rows go in one SaveChanges, so a row the database refuses takes the others with it.
- **A rental item's watch log is saved once.** It's empty, or it's what was answered. Nothing edits a saved one or adds to it, so a second answer for the same rental item adds nothing.
- **An empty one can be filled in later.** Scanning a case that's already on the shelf asks who watched its last rental, the one with the highest Id, for as long as that rental's watch log is empty. That covers Esc pressed by mistake, a clerk who left the screen, a crash, and ADR-0011's check-in that commits as the clerk leaves.
- **Skipping saves nothing.** So a skipped watch log and one that was never asked for read the same, and the rows stay the only record of who watched.

## Alternatives

- **Save the return and the watch log together, at the end.** It's how Checkout and Intake work, and a return would be one save. But the case would stay checked out until the clerk answered. Esc, leaving the screen or a crash would lose a return that really happened, and the next Checkout would refuse the case (GEN-7). ADR-0011's check-in that commits as the clerk leaves would become what every Esc does.
- **A column that marks a rental as asked or skipped.** It would tell a skip from a question never asked, so a rescan could ask only about those. But it's a second fact to keep in step with the rows, which ADR-0009 and ADR-0010 turned down: a hand edit could leave a rental marked skipped with rows, or marked asked with none. And telling them apart isn't worth it, since a rescan after a skip costs one Esc.
- **A list of unlogged returns on the screen.** The clerk could fill in any of them, even one rented again since. But skipping is fine, so the list would grow with every rental nobody wants to rate, and nothing would ever clear it. It's a screen and a query that Phase 3 doesn't need (GEN-5), and the rescan already reaches the one that matters: the case in the clerk's hands.
- **Let a saved watch log be edited.** A wrong star or a forgotten person could be fixed. But the watch log is the heirloom, and a saved one is history: taking a person off would delete it (DATA-4), and changing a star would overwrite what was said at the counter. An edit would also need a screen to find the rental and rules for what it may change. If the family wants one, a new ADR would supersede this one.

## Consequences

- **Phase 5.5's return-box count goes down at the check-in,** the save at the scan, not when the clerk answers who watched. A scan of a case that's already on the shelf checks nothing in, so it doesn't count.
- **A rescan after a skip on purpose asks again.** Skipping saves nothing, so Rewind can't tell it from a question never asked, and the clerk presses Esc again.
- **Once a case is rented again, its earlier rental's empty watch log is out of Return's reach.** Only a case's last rental is asked about, so a skipped watch log stays empty for good once the case goes out again. Nothing else writes the watch log.
- **"Saved once" is Core's check, since the unique index only stops one person twice.** `IX_WatchLog_RentalItemId_MemberId` refuses a member twice on one rental item, but a second answer that names someone else passes it. A CHECK can't see other rows, and a trigger would be raw SQL, which ADR-0009 keeps out of the rules. So `ReturnDesk` looks for any row inside its transaction. EF Core begins that as BEGIN IMMEDIATE (ADR-0006), so a second answer can't land between the look and the save. A hand edit in the SQLite shell can still add to a saved one.
- **The open rental item is what says a case is out, and the case's status follows it.** Cases.Status is a second fact of the kind this ADR turns down for watch logs, so this is where Rewind picks which wins. Checkout and Return each change both in one transaction. Where a hand edit leaves them out of step, the scan puts it right and logs a warning: a case On shelf with its rental open is checked in, and one checked out with no rental out goes back on the shelf with nobody asked. A retired case is refused either way.
- **A watch log filled in later still belongs to its rental's night.** A viewing has no time of its own, so whenever the clerk answers, the movie was watched on the night it was rented.
- **The clerk handbook will say that rescanning a case asks again** while nobody has answered for its last rental, when the Return screen arrives in the pull requests that follow.
