# ADR-0005: Intake saves at the label scan

- **Status:** Accepted
- **Date:** 2026-10-24
- **Author:** Ethan

## Context

Build Plan 2.3 saved each movie the moment the clerk picked it from TMDB's matches, with its cast, genres and posters, and the Intake screen then showed what was saved. Step 2.4 adds the physical case. The clerk adds each movie on the disc, one for a normal DVD and several for a box set or a double feature, then peels the next store label, sticks it on the case and scans it, and the case goes on the shelf. The Build Plan asks for the movie, the case and their links in one SaveChanges (DATA-7).

Saving each movie at its pick doesn't fit that:

- A wrong pick, or a box set the clerk gives up on halfway, would leave a movie that no case holds. Nothing is ever deleted (DATA-4), and Ask the Clerk and the phone catalog treat every movie as one the family owns, so it would show as owned for good.
- The movie and its case would be two saves, and a case that failed to save would leave its movie behind.
- Esc would only look like it starts over: the movies picked before it would stay in the collection.

## Decision

Nothing is saved until the store label is scanned.

- Picking a movie fetches its details from TMDB, with its posters at both of the sizes Rewind saves, and holds them in memory as one movie of the case being entered. A movie the collection already has is read from the database instead, so the case joins it. Neither writes anything.
- A movie TMDB doesn't have is typed in by hand, checked by Core, and held the same way.
- The label scan saves it all in one SaveChanges: the new movies with their cast and genres, the case On shelf with its label and factory barcode, and its links to every movie, new or already owned. The new movies' poster files are written after that save, so a save that fails leaves no files behind. They're written even if the clerk has just left the screen, so a case that's on the shelf is never reported as cancelled, and a poster that can't be written leaves its movie without it for now (GEN-7).
- While the case is open, the screen lists its movies and its barcode. That list is the confirmation: the label scan saves without asking again, since a batch of 20 DVDs shouldn't cost 40 extra keystrokes.
- A case is open from its first scan or pick until its label scan. Esc, or leaving the Intake screen, abandons it, and nothing of it has been saved.
- The label is checked before the save, so a label that's in use is an outcome the screen reports, not a failed save (NET-12). The unique index on Cases.StoreLabel is still the real guard (DATA-11): a label another case takes between the check and the save is reported the same way.

## Alternatives

- **Save each movie at its pick, as 2.3 did, and the case alone at the label scan.** The screen would show each movie exactly as saved, and a pick would be done with TMDB for good. But a wrong pick or an abandoned box set would leave movies with no case, which nothing can delete, and the case would be a second save.
- **Ask before saving, with a dialog at the label scan that lists what the label will bind.** It's the last place a mismatch shows, but the list on screen shows it already, before the scan, and a dialog for every case doubles the keystrokes of a batch.
- **Keep the open case in a service, so it survives a switch to another screen.** Intake runs in batches at the counter, and a case started over costs one scan and one pick. Rewind builds only what the phase needs (GEN-5).

## Consequences

- Core gets the pick, the hand entry and the label scan's save first, beside 2.3's AddAsync. The Intake screen switches over to them in the pull request that follows, and AddAsync goes once nothing calls it.
- From then on, a wrong pick costs nothing, and Esc really does start over.
- A new movie's posters are held in memory until the label scan: about 50 KB a movie, a few hundred KB for a box set.
- Build Plan 2.3's check moves one step later: picking Halloween (1978) fetches the right poster, director, cast and rating, and the label scan saves them. The Build Plan's 2.3 and 2.4 and the Overview's intake steps say so.
- A movie's TmdbFetchedAt is when TMDB answered the pick, not when the case was saved, since TMDB's six months count from the fetch.
- The same TMDB movie picked twice for one case is saved once. The screen won't list a movie twice, and Core drops a repeat anyway.
- Leaving Intake with a case open loses the case, and the clerk handbook will say so when the screen changes in the pull request that follows.
