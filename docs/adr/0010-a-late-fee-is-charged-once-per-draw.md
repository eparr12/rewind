# ADR-0010: A late fee is charged once per draw

- **Status:** Accepted
- **Date:** 2026-11-03
- **Author:** Ethan

## Context

A member draws one card a night from their late-fee bag, and rescanning their card returns the same draw, so a rescan can't fish for a Clear (Build Plan 3.2). That left a question for Checkout: a member who checks out twice in one night gets the same draw back, so what does an Overdue card do the second time?

The Overview answers part of it: late fees are "at most once per movie night". It doesn't say when the fee counts as charged, and that matters, because a sale can be cancelled after the alert has shown. Nothing was saved then, and nothing was paid.

## Decision

The fee is charged once per draw, on the first sale that completes.

- **A fee line is what says the fee is paid.** A sale line's `LateFeeDrawId` points at the draw it pays for, and that's the only record of it. Nothing else holds the fact, so nothing else can disagree with it.
- **The alert follows from that.** The alert shows, and the fee goes on the bill, only while an Overdue draw has no fee line.
- **A cancelled sale leaves the fee unpaid.** Nothing was saved, so the next scan of the card alerts and charges again.
- **A second checkout after a paid fee gets no alert and no fee.**
- **The database holds the rule (DATA-11).** `SaleLines.LateFeeDrawId` has a unique index. SQLite treats NULLs as distinct in a unique index, so snack lines, which have no draw, don't collide: the same fact ADR-0009 had to work around is what this one wants. A CHECK holds each line to exactly one of a snack and a draw.

## Alternatives

- **Charge at every checkout.** It's the simplest, but it breaks the Overview's "at most once per movie night": a member who comes back for a second movie would pay again.
- **Charge at the draw, rather than the sale.** The draw comes first, so there's no rental yet for a line to be on. A sale cancelled after the alert would already have charged, and the member would owe for a sale that never happened.
- **Leave it to the screen.** Checkout could remember that it charged a fee. But the screen keeps an open sale in its own fields (ADR-0003) and forgets it when the clerk leaves, and DATA-11 says a rule the database can hold, it holds.
- **A paid flag on the draw.** It would make the question one read of one row, but it's a second fact to keep in step with the fee line, which is ADR-0009's objection to a `bool` column kept in step with `ClosedAt`. A repair by hand could leave a paid draw with no line, or a line on an unpaid draw that would charge again.

## Consequences

- **A paid fee can't fire again on a stale night.** A skipped Close Out leaves last week's night open, so next week's first scan gets last week's draw back (#59). Once that draw's fee is paid, it stays paid. The rest of #59 stays with #59: the bag not moving, rentals landing on last week's night, and the backup that never ran.
- **An unpaid fee can.** A sale cancelled last week on a night nobody closed still leaves the fee unpaid, so the alert fires again. That's #59 too.
- **What the database can't hold:** that a fee line's draw belongs to this member, or that it was Overdue. A CHECK can't cross tables, so that rests on Checkout's Start, which only offers a fee line for the member's own Overdue draw.
- **A refused second fee line is a bug, not a race.** One clerk can't complete two sales at once, so a second line for one draw means a second register or a mistake in the code. Completing a sale lets that error through rather than treating it as an expected result (NET-12).
- **The fee on the line is the fee at the sale** (DATA-10), so a later change to the late fee doesn't touch a paid line, and a cancelled sale's next alert charges the late fee as it is by then.
