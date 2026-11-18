# ADR-0014: A night nobody closed ends at the next night's first card

- **Status:** Accepted
- **Date:** 2026-11-17
- **Author:** Ethan

## Context

A movie night is one business day. The first member card scanned at Checkout opens it and Close Out closes it (Build Plan 3.2), and until then `IBusinessDays.OpenOrCurrentAsync` gives back the night that's open, however late it gets. That's what lets a night run past midnight (DATA-8). But Close Out is an easy step to forget, and a night nobody closed out stays open until the next Close Out, whenever that is (ADR-0013).

Since #57, each member's draw is saved against the night, and a rescan the same night gets the same draw back, so it can't fish for a Clear. So after a skipped Close Out, next week's first scan of a card finds last week's night (#59):

- **The same draw comes back,** a card drawn a week ago.
- **An unpaid Overdue fee alerts and goes on the bill again** (ADR-0010).
- **The member's bag doesn't move,** since nothing new is drawn, and their one card in four stalls for the week.
- **Whatever's rented goes on last week's night,** and the backup and the catalog that Close Out would have made never happened.

#59 asked for a decision before Build Plan 3.6's first movie night, and listed what it has to settle: what makes a night stale without ending a real night early, which rules out a calendar date (DATA-8); what time goes in `ClosedAt`, and whether a night closed that way looks any different from one that went through Close Out; whether the skipped backup and catalog run then or wait; and what the clerk sees.

## Decision

A night nobody closed out ends at the next night's first card.

- **The first member card scanned at Checkout more than 16 hours after the open night opened ends that night and opens a new one,** and the card draws on the new one. Nothing else ends a night this way: a return still opens and closes none, and Close Out still closes whatever is open, however old it is (ADR-0013).
- **What makes a night stale is the 16 hours since it opened.** It's a named constant in Core (GEN-4), measured from `OpenedAt` on the injected `TimeProvider` (NET-8), never from a calendar date (DATA-8). At exactly 16 hours it's still the same night. No movie night lasts that long, even one that runs past midnight, and the next evening's first card is further away than that.
- **`ClosedAt` is the clock's time at that scan,** which is when the night was closed. Nothing else marks a night closed this way: there's no new column and no migration, since nothing reads how a night was closed (GEN-5). A later reader tells by the next night: the stale night's `ClosedAt` equals the next night's `OpenedAt`, since both come from one reading of the clock. A long gap between a night's `OpenedAt` and its `ClosedAt` isn't the mark: a late Close Out leaves one too, and a stale night's can be under a day.
- **The close and the new night save together or not at all (DATA-7),** in one save, with the close first, so the one-open-night index never sees two open nights. Two scans that both find the stale night race as two that find none do (ADR-0009): the index refuses the second one's new night, which takes its close with it, and that scan gets the first one's night.
- **Nothing extra runs at the scan.** The backup, the catalog and the copy that were skipped wait for the next Close Out. The stale night's rows are still in the database, so that Close Out backs them up, and a card scan must never wait on a backup or fail because of one (GEN-7). The status bar's Last backup day already shows the miss (OBS-6).
- **The clerk sees nothing new.** The card starts its sale like any night's first. One Warning in the log names the night and when it opened, in local time (OBS-3), in place of the Information line a Close Out's closing writes.

## Alternatives

- **Only Close Out ends a night, with an opening check in the handbook.** Before the first card, the clerk would check Last backup on the status bar, and close out first if it's older than the last movie night. Close Out would stay the only way a night ends, and the backup would be made before anyone checks out. But it's a step to remember at the start of the night, to make up for one forgotten at the end, and missing it brings back all of #59 with no sign. It would also take room on the front of the sheet, which #84 is saving for Ask the Clerk and the return box.
- **Closing a stale night at startup.** Last week's night would end before anyone scanned a card. But a night nobody closed out is likely one nobody shut Rewind down after either, and the PC never sleeps (Build Plan 6.1), so Rewind can still be running at next week's first card, with no start in between. And a start partway through a night, such as after a crash, would need the 16 hours anyway, so startup would only be a second place for the same rule.
- **A calendar date.** A night opened before today would be stale. But a night that runs past midnight would end at its first card after midnight, which DATA-8 and `OpenOrCurrentAsync_PastMidnight_ReturnsTheSameNight` rule out. Starting the day in the early morning instead would fix that, but it's still a calendar rule, which DATA-8 keeps out of movie-night logic: it needs the register's time zone to say what day it is, where 16 hours needs only the two instants.
- **Time since the night's last draw or sale, rather than since it opened.** A night would last as long as it stays busy. But it's another read on every card, of the draws and the rentals, and no length of quiet works both ways. A short one, of a few hours, would give a next-morning session a night of its own, which this decision concedes it doesn't get, but it would end a real night at any quiet stretch that long, such as between a morning session and the evening's. A long one, such as 16 hours, would let a night last as long as nobody leaves that gap: a Saturday morning session would join Friday's night, and a Saturday evening one the morning's, so a weekend could be one night, with one draw each. `OpenedAt` is one fact the row already has, and it never moves.
- **Asking the clerk in a dialog,** such as "Last movie night was never closed out. Close it now?" But the clerk is a kid with a family waiting, it's a question for the Store Manager, and only one answer is right: No brings #59 back. The night's first card would wait on a dialog, too, for a rule Core can decide alone.
- **Running the backup at the scan.** The missed night would be backed up as soon as it closed. But the scan would wait on a backup, which grows with the database, and a backup can fail, such as on a full disk: a card scan must never wait on a backup or fail because of one (GEN-7). Waiting loses nothing, since the next Close Out's backup holds the rows.
- **A column that marks a night Rewind closed.** A later reader could tell at a glance which nights went through Close Out. But nothing reads it (GEN-5), it needs a migration, and it's a second fact to keep in step with `ClosedAt`, which ADR-0009, ADR-0010 and ADR-0013 each turned down. The next night already says the same, and exactly: a night Rewind closed has a `ClosedAt` equal to the next night's `OpenedAt`. The Warning names the night in the log, too.

## Consequences

- **An Overdue fee left unpaid on the stale night is never charged.** Checkout charges only the fee for the member's draw on the open night (ADR-0010), and once the stale night is closed, that's the new draw.
- **The stale night's rentals stay on it,** with its draws. Nothing moves to the new night.
- **The member's bag moves on with the new draw,** as it does after a Close Out.
- **A session that starts within 16 hours of an unclosed night's opening, such as the next morning, still joins it.** Its cards get that night's draws back, and its rentals go on it. Once the 16 hours are up, the next card ends the night, and from then on each member draws again, those who drew that morning too.
- **The rule trusts the register's clock (NET-8).** Set back to before a night opened, the clock makes that night look newer than it is, so it stays open, however old it really is, until Close Out closes it, as every night did before this ADR. Set far ahead, it ends tonight's night at the next card. Either way, Close Out still closes whatever is open (ADR-0013).
- **Two of ADR-0010's consequences change.** "A paid fee can't fire again on a stale night" and "An unpaid fee can" both rest on next week's first scan getting last week's draw back. More than 16 hours on, it no longer does: the card ends last week's night and draws again, so an unpaid fee doesn't fire again, and the bag, the rentals and the backup that ADR-0010 left to #59 are settled here.
- **ADR-0013's last consequence changes.** A stale night is no longer closed by the next Close Out, whenever that is, but by the first card more than 16 hours after it opened, unless a Close Out comes first.
- **The Overview and the Build Plan change when the three documents next catch up.** The Overview's "Checkout, returns and late fees" and its BusinessDays row, and Build Plan 3.2, each say a night ends at Close Out.
