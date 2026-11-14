# ADR-0011: Scanning screens share one base class

- **Status:** Accepted
- **Date:** 2026-11-13
- **Author:** Ethan

## Context

Four components listen to the scanner: Intake (F4), Checkout (F1), F5's Members tab and the status bar. The first three handle a scan the same way, and the status bar has a cousin of their handler. Return (Build Plan 3.4) would make the fourth copy. GEN-8 says to extract shared code on the third use, and also to prefer a little duplication over the wrong abstraction, so #70 asked which it should be before Return copies the handler again.

Each copy carries the same parts, about 50 lines in Intake and in Checkout:

- **A `CancellationTokenSource`** that `Dispose` cancels and disposes. Every call the screen makes to Core takes its token (NET-7).
- **`Scanner.Scanned +=`** in `OnInitialized`, before the screen's first read, and `-=` in `Dispose`.
- **`OnScanned`,** which moves the scan onto the renderer's thread with `InvokeAsync` (UI-11). It sends what the scan's work throws to ErrorHub once the clerk has left, and to `DispatchExceptionAsync` and the screen's ErrorBoundary while the screen is open.
- **A late-scan guard.** A scan raised just before the clerk left still arrives, and reading the disposed source's token would throw, so each handler checks the source first.
- **In Intake and Checkout, `WhileBusyAsync`,** which shows the busy line while Core answers and turns leaving's cancellation into null, and **`ShowAsync`,** which draws the screen before a dialog.

They differ in what a scan means and what's refused, and in where leaving's cancellation is caught: Intake and Checkout catch it in `WhileBusyAsync`, the Members tab and the status bar in a catch of their own. The status bar also sends every failure to ErrorHub, open or not, since its ErrorBoundary would close the bar over a name (UI-11).

An answer that isn't a cancellation can also arrive after the clerk has left, and the code after the call still runs. Two calls guard against that, each with a test: the Members tab's add, which drops its answer, and Checkout's snack lookup, which shows nothing. Every other call whose answer can lead to a dialog is unguarded: Intake's TMDB search and pick and its label scan's save, and Checkout's card, case and sale. A dialog one of them shows opens over the next screen, because DialogService outlives the screen. And it doesn't only show. DialogService lets a new dialog replace whichever one is open, which then counts as cancelled, so a question the clerk is answering on the next screen, such as Shut Down's, answers Cancel without them. Three tests written for this show it on main: a label scan's save that answers "label in use", a search that answers that TMDB didn't, and a card's lookup that answers with an Overdue draw, which brings the error chord and the OVERDUE RENTAL alert.

On the register, the database can't answer late. SQLite answers before the renderer can run anything else, as #70 says, so Checkout's calls, the Members tab's and Intake's label scan's save are safe there. TMDB can answer late. Core never uses `ConfigureAwait(false)`, so its continuations wait their turn on the renderer's thread, and the F-key that leaves the screen can come first. TmdbClient checks the token when a call fails, so a search that times out as the clerk leaves ends as a cancellation. It doesn't check once TMDB has replied. A refusal that's in already when the clerk presses the F-key, such as a 429, a 5xx, a 401 or a 404, or a reply that isn't JSON, comes back as TryLater, NotSetUp or NotFound, and Intake's search and pick then show their dialog over the next screen. The reply has to arrive in the moment before the F-key's turn, so it's rare, but it can happen. Return adds two more places it could happen: its refusals, and anything that asks who watched.

#70's options 1 and 2 were each tried on a throwaway branch. Intake and Checkout moved onto each one, with every existing test left as it was. All 507 UI tests passed on each, with no warnings.

## Decision

Every screen that takes scans inherits one base component, `ScanningScreen` in `Rewind.UI.Screens`, through `@inherits`. The screen supplies only what a scan means to it, by overriding `OnScanAsync`. The base owns the rest:

- **The token that leaving cancels,** as `Closing`. It's read once, when the screen is made, so it can still be read after the clerk leaves. A call made then is cancelled at once, rather than throwing because its source has been disposed.
- **Listening,** from `OnInitialized` until `Dispose`. `OnInitialized` is sealed, so no screen can skip it, and listening always starts before the first read a screen starts in `OnInitializedAsync`. A screen that overrides `OnInitialized` doesn't compile (CS0239), and neither does one that writes its own `Dispose` (CS0108, an error under NET-2).
- **The late-scan guard.** A scan that arrives once the clerk has left never reaches `OnScanAsync`.
- **Three routes for what a scan's work throws.** Together they cover what the three screens' copies do. Leaving's cancellation goes nowhere. Any other exception after the clerk has left goes to `ErrorHub.ReportFromComponent`, named for the screen's type. While the screen is open, everything goes to `DispatchExceptionAsync` and the screen's ErrorBoundary.
- **The busy line:** `Doing`, `Busy` and `WhileBusyAsync`. `WhileBusyAsync` shows what Rewind is doing until Core answers, and returns null when the call was cancelled. Any other answer comes back as it came, even after the clerk has left.
- **The dialogs, which a screen shows only through `ShowAsync`.** DialogService is private to the base. A screen gets `ShowAsync`, and `DialogOpen` for whether a dialog is waiting for an answer, which is all the three read from the service. `ShowAsync` draws the screen first. Once the clerk has left, it shows nothing and answers as Cancel does. A screen that injects DialogService itself still compiles, but it has to add that line on purpose to get around the check.

Only the dialog is refused. What else a screen does with an answer that came after the clerk left is its own. A committed sale or case still chimes, since it's done (NET-7). The Members tab drops its late answer, chime and all, as it did before.

Intake, Checkout and F5's Members tab move onto the base, and Return is built on it in Build Plan 3.4. That makes four users, so the base isn't speculative (GEN-5), and it comes after GEN-8's third use. The Members tab keeps its own save flag, `_saving`, which disables its boxes rather than showing a busy line.

The status bar stays as it is. It isn't a screen: it's open as long as the shell, and UI-11 sends its lookups' failures to ErrorHub even while it's open, which is the one route the base doesn't take.

## Alternatives

- **A helper each screen holds** (#70's option 2). Tried on a branch, it works, but a helper can't reach a component's own `InvokeAsync`, `DispatchExceptionAsync` or `StateHasChanged`, so each screen hands them over as delegates.
  - Owning the source and the catches, as #70 put it, takes six arguments. It leaves `WhileBusyAsync` and `ShowAsync` in each screen, where Return makes the third copy, and each `ShowAsync` has to remember the check that keeps a late dialog off the next screen.
  - Taking those as well takes eight arguments and forwarders back into the screen. The helper then draws the screen itself, the other way round from ADR-0003's plain classes, whose component calls `StateHasChanged`.
  - Either way, each screen still writes `@implements IDisposable`, injects the scanner and ErrorHub only to pass them on, and starts the helper in `OnInitialized`, with nothing to stop a screen forgetting.
- **Keep the copies** (#70's option 3). There's nothing new to learn, and each screen reads top to bottom. But Return's copy would be about 50 lines with ten things to get right by hand, from subscribing before the first await to naming itself in the ErrorHub call. And #70's gap stays open in every copy until each dialog after an await is guarded by hand.
- **A child component in the screen's markup,** such as `<ScanListener OnScan="OnScanAsync" />`. #70 doesn't list it. The helper's spike pointed to it, since a child component has its own `InvokeAsync` and `DispatchExceptionAsync`, and those reach the screen's ErrorBoundary. It wasn't tried. The screen would still need a source of its own for its other calls, so the gap stays per screen. And listening would start at the screen's first render, after its first read has begun.
- **Closing the gap at the answer,** with `WhileBusyAsync` returning null for any answer after the clerk left. It's simpler to say, but it turns a done outcome into nothing: Checkout's committed sale would no longer chime, which `Dispose_SaveFinishesAnyway_StillChimes` pins (NET-7).

## Consequences

- **Rewind.UI has its first `@inherits`.** To see how a scan arrives and where its errors go, a reader of a scanning screen follows one hop to `ScanningScreen`. UI-11's rules are written out there once, rather than in each screen.
- **ADR-0003 stands.** Apart from the busy line, the base holds plumbing, not a screen's state. Each screen's own fields still hold what it shows.
- **The line counts:** Intake goes from 641 lines to 569, Checkout from 548 to 479 and the Members tab from 342 to 296. The base is 145 lines, 79 of them code and the rest its comments. Return's handler is its own logic and nothing else.
- **No screen has its own `OnInitialized`.** It starts its reads in `OnInitializedAsync`, as all three do now. If a screen someday needs cleanup of its own, it gets a protected hook in the base then, not before (GEN-5).
- **The existing tests pin the base through the screens.** Breaking its guard, any of its three routes, the dialog check or the unsubscribe fails at least one screen's tests. The cancellation route is pinned only by the Members tab's, since Intake and Checkout catch the cancellation in `WhileBusyAsync` first.
- **All three screens pin the late-scan guard.** Each screen's `Dispose_ScanRaisedJustBefore_RaisesNothing` asserts that a scan raised just before the clerk left asks Core nothing. Before, Intake's and the Members tab's caught a missing guard only because reading a disposed source's token threw, and `Closing` doesn't throw.
- **Three tests pin the closed gap:** `Dispose_ShelvingAnswersAfterTheClerkLeft_ShowsNoDialog` and `Dispose_SearchAnswersAfterTheClerkLeft_ShowsNoDialog` for Intake, and `Dispose_CardAnswersAfterTheClerkLeft_ShowsNoDialog` for Checkout.
- **UI-11 gains a sentence:** "A screen that takes scans inherits ScanningScreen, which does all of this for it, and shows no dialog once the clerk has left it (ADR-0011)."
- **A check-in that commits as the clerk leaves Return** puts the case on the shelf without asking who watched. Whether that rental's watch log can be filled in later is Build Plan 3.4's to decide.
