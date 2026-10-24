// The register's one JavaScript module: keyboard, focus and printing (UI-9).

const functionKey = /^F[1-5]$/;

// The keys that move through a list, as in a Windows 98 list view.
const listKeys = ['ArrowUp', 'ArrowDown', 'Home', 'End'];

// The shell that F1 to F5 go to. After a crash, App starts a new shell, which takes its place.
let shell = null;

// The element that last had the focus outside a dialog, for the focus to go back to.
let focusBeforeDialog = null;

// Each shell calls this once, after its first render. The listeners outlive the shell that
// added them, so a new shell after a crash only takes over the F-keys.
export function start(dotNetShell) {
  const listening = shell !== null;
  shell = dotNetShell;
  if (listening) {
    return;
  }

  // F1 to F5 switch screens wherever the focus is (UI-3).
  document.addEventListener('keydown', e => {
    if (!functionKey.test(e.key) || e.altKey || e.ctrlKey || e.shiftKey || e.metaKey) {
      return; // Alt+F4 isn't the Intake key
    }
    e.preventDefault(); // F5 never reloads, F3 never opens Find
    shell.invokeMethodAsync('OnFunctionKey', e.key);
  });

  document.addEventListener('keydown', keepTabInDialog);
  document.addEventListener('keydown', moveInList);
  document.addEventListener('focusin', e => {
    if (e.target instanceof Element && !e.target.closest('.dialog')) {
      focusBeforeDialog = e.target;
    }
  });

  // SEC-6: BlazorWebView opens the default browser for a middle-click, Ctrl+click or
  // Shift+click on an href link, and MainForm can't stop it. Rewind's pages use no href
  // links; if one slips in anyway, clicking it does nothing. Capturing runs this before
  // Blazor's own link handling, which then leaves the click alone.
  document.addEventListener('click', ignoreHrefLinks, true);
  document.addEventListener('auxclick', ignoreHrefLinks, true); // the middle button
}

// The dialog host calls this once a dialog closes. As in Windows 98, the focus goes back to
// where the clerk was, such as the button that opened the dialog, if it's still there.
export function restoreFocus() {
  if (focusBeforeDialog?.isConnected) {
    focusBeforeDialog.focus();
  }
}

// UI-3: as in Windows 98, Tab and Shift+Tab go round an open dialog's buttons and never leave
// it. The screen behind is inert, but Tab could still reach the scan simulator or leave the page.
function keepTabInDialog(e) {
  const dialog = document.querySelector('.dialog');
  if (e.key !== 'Tab' || dialog === null) {
    return;
  }

  const stops = [...dialog.querySelectorAll('button, input, select, textarea')]
    .filter(control => control.tabIndex >= 0 && !control.disabled);
  if (stops.length === 0) {
    return;
  }

  e.preventDefault();
  const here = stops.indexOf(document.activeElement); // -1 when the focus is outside the dialog
  let next;
  if (e.shiftKey) {
    next = here <= 0 ? stops.length - 1 : here - 1;
  } else {
    next = (here + 1) % stops.length;
  }
  stops[next].focus();
}

// UI-3: in a list, which is a 98.css interactive table, the arrow keys move the focus from row to
// row, and Home and End to the first and last, as in a Windows 98 list view. Tab moves on to the
// next control. The screen highlights whichever row has the focus.
function moveInList(e) {
  const row = e.target instanceof Element ? e.target.closest('table.interactive > tbody > tr') : null;
  if (row === null || !listKeys.includes(e.key) || e.altKey || e.ctrlKey || e.shiftKey || e.metaKey) {
    return;
  }

  const rows = [...row.parentElement.children];
  const here = rows.indexOf(row);
  const next = {
    ArrowUp: Math.max(here - 1, 0),
    ArrowDown: Math.min(here + 1, rows.length - 1),
    Home: 0,
    End: rows.length - 1,
  }[e.key];
  e.preventDefault(); // move by a row, rather than scroll the list as well
  rows[next].focus(); // which scrolls the row into view
}

function ignoreHrefLinks(e) {
  if (e.target instanceof Element && e.target.closest('a[href]')) {
    e.preventDefault();
  }
}
