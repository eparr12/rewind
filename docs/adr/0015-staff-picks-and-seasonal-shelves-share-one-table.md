# ADR-0015: Staff Picks and seasonal shelves share one table

- **Status:** Accepted
- **Date:** 2026-11-20
- **Author:** Ethan

## Context

Build Plan 4.2 adds shelves to Ask the Clerk: Shelves and ShelfMovies, with a Staff Picks shelf for each member and seasonal shelves that appear in season. One movie can sit on several shelves, and each family member keeps their own picks. Seasonal shelves carry a start and end date by month and day, "so Halloween shows up every October without anyone touching it", and shelves are family data, so a TMDB refresh never touches them (DATA-9). The step is done when The Nightmare Before Christmas sits on both the Halloween and the Christmas shelves. The Overview's data model gives the two tables one row, "A movie can sit on many shelves", and its open decisions still ask which seasonal shelves there are, and the dates each one shows up.

That leaves these to settle before the tables are made:

- **What tells a Staff Picks shelf from a seasonal one,** and whether they're one table or two.
- **When a member's shelf comes to be,** for a new member and for the members the register already has.
- **What a season is,** when it comes round every year, can run over New Year, and can take in February 29.
- **Which day decides what's in season,** when a movie night runs past midnight (DATA-8).
- **Who keeps the seasonal shelves,** which ones there are, and whether a shelf is ever deleted (DATA-4).
- **What ShelfMovies keeps** once a movie comes off a shelf.
- **Who may put a movie on a member's shelf,** when the Build Plan has each member add to "their own".

## Decision

Staff Picks and seasonal shelves share one table, and a shelf's member says which kind it is.

- **One table holds both kinds of shelf.** A shelf with a member is that member's Staff Picks. A shelf without one is seasonal, with a name and a season. No column says which kind a shelf is, since the member already does. One CHECK holds each kind to its columns: a member's shelf has no name and no season, and is always active, and a seasonal shelf has a name and all four numbers of its season. ShelfMovies points at a shelf of either kind the same way.
- **Every member has a Staff Picks shelf, from the save that adds them.** The Members tab's add saves the member and their shelf in one SaveChanges (DATA-7), and a unique index keeps one shelf for each member. The shelf has no name of its own: it's named for its member, so a rename follows. Nothing deactivates it by itself either: it's hidden while its member is deactivated, and back when they are.
  - **The members already in the database get theirs in AddShelves,** the migration that makes the tables, with one INSERT that selects from Members, active or not. It's the first raw SQL in a migration, which ADR-0009 allows: it moves data and holds no rule, and the rules it meets, such as one shelf for each member, are in the model.
- **A season is a first day and a last day, each a month and a day, with no year.** The first may come after the last, for a season that runs over New Year, such as December 1 to January 6. Each end is a real day: a month from 1 to 12, and a day from 1 to that month's last, with 29 for February, since February 29 is a day. A day is in a season from its first day to its last, both taken in, running on past December 31 when the first comes after the last.
- **A season goes by the movie night (DATA-8).** While a night is open, a shelf is in season when the day that night opened, on the register's clock, is in its season, so the Halloween shelf lasts until Halloween night's Close Out, not until midnight. With no night open, it goes by the register's date. Core's service for the shelves builds this.
- **Seasonal shelves are the Store Manager's rows.** A Shelves tab on F5 will add, rename, re-date and deactivate them, and nothing deletes one (DATA-4). None is built in, which settles the Overview's open decision: the family picks which shelves there are, and the dates each one shows up.
  - **No two share a name, whatever its letter case.** The name's column is NOCASE, which folds A to Z only (#100).
  - **A deactivated shelf keeps its name,** so it's reactivated rather than added again, as a deactivated member keeps their card.
- **ShelfMovies holds what's on each shelf now.** Its key is the shelf and the movie, so a movie sits on a shelf once. Taking a movie off a shelf deletes its row, as a refresh replaces a movie's genres. It isn't history, so DATA-4 doesn't keep it, and the log will have a line for each change. Every foreign key is still Restrict, so neither a shelf with a movie on it nor a movie on a shelf can be deleted.
- **Anyone at the register can put a movie on any shelf.** "Their own" is the family's rule, not the register's, so no screen asks for a member card.
- **Shelves are the family's (DATA-9).** Nothing here touches Movies, MovieCast, Genres or MovieGenres, and Build Plan 4.5's refresh will write to neither new table.

## Alternatives

- **A column that says which kind a shelf is,** such as a `Kind` of Staff Picks or Seasonal. A reader in the SQLite shell would see the kind at a glance. But the member already says it, so the column would be a second fact to keep in step, which ADR-0009, ADR-0010, ADR-0012, ADR-0013 and ADR-0014 each turned down: a row marked seasonal that has a member, or Staff Picks without one, would say two things at once. The CHECK would have to refuse both, so the column could only ever repeat what the member says.
- **A table for each kind, or no row at all for a Staff Picks shelf,** a pick then being a member and a movie. Each table would hold only its own columns, with no NULLs and no CHECK between the kinds. But a movie on a shelf would then need a link table for each kind, since one foreign key can't point at either of two tables. Every read that lists the shelves side by side, and the spinner's filter by shelf (Build Plan 4.3), would ask two tables and put their answers together. With no row for a Staff Picks shelf, one pick would point at a member and another at a shelf, and "a shelf" would be two kinds of thing everywhere it's read.
- **Making a member's shelf at their first pick.** No shelf would sit empty, and adding a member wouldn't change. But until that first pick, a member would have no Staff Picks to show, so every screen that lists the shelves would have to make one up for them, and putting a movie on a shelf would sometimes write one row and sometimes two. A shelf from the save that adds the member is a rule with no exceptions, and AddShelves gives the members already there theirs.
- **Building Halloween and Christmas into the migration,** with dates such as October 1 to 31 and December 1 to 25, as the Overview's "such as Halloween and Christmas" suggests. They'd be there on the first start. But which shelves there are, and their dates, are the family's to choose, and that's the open decision this settles. Built in, they'd be Rewind's choice, in a migration that can never change once it has run on the register (DATA-5), and a family that wanted other shelves or other dates would start by undoing Rewind's. Adding them on F5's Shelves tab will take a minute and need no release.
- **A season as text, or as a day of the year,** such as `10-01` to `10-31`, or 274 to 304. Either is one value for each end. But text needs a CHECK on its form as well as on its day, and sorts wrong as soon as one is written `10-1`. A day of the year moves with leap years: days 274 to 304 are October 1 to 31 in most years, but September 30 to October 30 in a leap year, when the Halloween shelf would be gone on Halloween itself, and day 60 is February 29 in a leap year and March 1 in the others. A month and a day are what the family says, what F5 will ask for, and what a reader in the SQLite shell can check against a calendar.
- **The register's date alone for what's in season.** It's one rule, with no read of the night. But a movie night runs past midnight (DATA-8), so the Halloween shelf would go at midnight on Halloween night, in the middle of the night it's for. DATA-8 keeps movie-night logic on BusinessDays for exactly that. The register's date stays for when no night is open, such as at intake or before the night's first card, since then there's no night to go by.
- **Keeping a row for a movie that was taken off a shelf,** with when it came off, so each shelf would have a history, as the watch log does. But nothing reads it (GEN-5), every read of a shelf would have to skip the old rows, and a movie put back on would need a second row, so the key could no longer hold a movie on a shelf once. A refresh replaces a movie's genres the same way, and the log will have a line for each change.
- **Asking for a member card before a movie goes on that member's shelf,** so only Dad could add to Dad's picks. But a card says whose it is, not who's holding it. At Checkout, a card is whose rental and draw it is (ADR-0010). Here it would be one more scan before every pick, which anyone holding Dad's card could still make, and which tells a family nothing it doesn't know already about whose shelf is whose.
- **A name on every shelf, the member's copied onto theirs.** Every shelf would have a name of its own to show. But it's a second fact to keep in step with the member's name: a rename on F5 would have to rename the shelf too, and a shelf renamed by itself would show a name its member doesn't have. Named for its member, a shelf follows a rename with nothing to update.

## Consequences

- **The Halloween shelf lasts until Halloween night's Close Out.** A night nobody closed keeps the shelves of the day it opened until a Close Out, or the next night's first card, ends it (ADR-0014).
- **In a year without February 29,** a season that starts on it starts on March 1, and one that ends on it ends on February 28.
- **A deactivated seasonal shelf keeps its name and its movies.** Reactivated, it's back as it was, and no new shelf can take its name.
- **Two names that differ only in an accented letter's case are two shelves,** such as Día de Muertos and DÍA DE MUERTOS (#100).
- **What was on a shelf before is only in the log,** as a member's old names are.
- **A member added by hand in the SQLite shell needs a shelf row too,** or they have no Staff Picks: only the Members tab's add and AddShelves make one. It's the row AddShelves adds for each member: `INSERT INTO "Shelves" ("MemberId", "IsActive") VALUES (<their Id>, 1);`.
- **ADR-0009's "No migration has any today" is no longer so.** AddShelves' INSERT is the first raw SQL in a migration, and the README's "Change the database" says how to add such a statement and try it on a copy of the register's database.
- **Build Plan 4.5's refresh test has two more tables to prove untouched,** Shelves and ShelfMovies.
- **The Overview's open decision on seasonal shelves closes, and DATA-11's list grows,** when the three documents next catch up with this step.
