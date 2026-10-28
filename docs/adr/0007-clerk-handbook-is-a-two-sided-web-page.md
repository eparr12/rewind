# ADR-0007: The clerk handbook is a two-sided web page

- **Status:** Accepted
- **Date:** 2026-10-28
- **Author:** Ethan

## Context

The clerk handbook is a laminated sheet at the counter, written for the kids (DOC-6). DOC-6 said it was one printable page, and it was a Markdown file, `docs/handbook/README.md`, that reviews checked by rendering it to a PDF.

By Phase 3 it no longer fit:

- **It was already longer than a page.** Printed from a browser at a true 11 pt on Letter, main's 705-word handbook ran to 1.9 pages, or 1.2 to 1.3 pages in a compact layout.
- **Whether it fit depended on the renderer.** It fit on one page only in pandoc and wkhtmltopdf, which lay text out about 1.9 times smaller than Chrome does. Markdown has no page size, so nothing in the file said how big it prints, and the renderer that said it fit isn't the one that prints the sheet.
- **Phase 3 adds more.** Checkout, Return and Close Out add about 250 to 350 words. Phase 4 adds Ask the Clerk, and Phase 5 the return box.

## Decision

- **Both sides of one sheet.** The front is for movie night: the keys, what to do when a message pops up, closing up, and Checkout, Return and Close Out as they arrive. The back is for the jobs behind the counter: moving around, adding a movie, printing labels and member cards, and scanning.
- **A web page with its own print styles.** `docs/handbook/handbook.html` sets Letter with 0.6 in margins, Segoe UI at 10.5 pt with a line height of 1.3, and the back on a page of its own. A Chromium browser prints it at the size it was measured at.
- **On screen, each side is a sheet of paper** the size of the printed page, so text that runs past a sheet's bottom edge won't fit. A note above the sheets, hidden in print, says how to print it.
- **DOC-6 says so:** "The clerk handbook, docs/handbook/handbook.html, prints on both sides of one sheet with its own print styles, movie night on the front."

## Alternatives

- **Stay on one side and cut.** Moving "Printing labels and member cards" to the README and trimming the rest by about a third frees roughly 250 to 300 words: just enough for Phase 3, with nothing left for Phase 4. Every screen PR would re-measure the page, and Phase 4 would force this decision again.
- **Two sheets, one for each job:** a kids' sheet for movie night, and a Store Manager's sheet for intake, printing labels and cards, members and prices. It's the simplest for the kids only if they never add movies, and the handbook's intake steps are written for them. It's also two sheets to laminate and keep at the counter.
- **Keep the Markdown, and make a PDF for each print.** The PDF is only as true as the renderer that makes it, so whether it fits would still depend on which one was used, and someone would have to make a PDF before every print. Committing the PDF instead would put a second copy beside the text, and the two would drift apart.

## Consequences

- **Measured in Chromium.** Whether it fits is checked on the page's own print styles in Chrome or Edge: printed to PDF, it has two pages. Each PR that changes it says how much room each side has left. When this was written, that was about 285 words on the front and 90 on the back. The README says how to check it.
- **GitHub shows the source, not the page.** To read it as the kids will, open the file in a browser. Reviews read its text in the diff.
- **Room for Phase 3, not Phase 4.** At 10.5 pt a side holds about 550 words, so the sheet holds about 1,100. When Phase 4 needs more, the grown-up jobs on the back (labels, member cards, members, prices and the concession menu) move to the README, which leaves the sheet to the kids.
- **Printing.** A printer that can't print both sides prints page 1, then page 2 on the back of the same sheet, and the page says how. Build Plan 3.6 prints and laminates it before the first movie night.
