// The register's one JavaScript module: keyboard, focus and printing (UI-9).

const functionKey = /^F[1-5]$/;

// The element that last had the focus outside a dialog, for the focus to go back to.
let focusBeforeDialog = null;

// The shell calls this once, after its first render.
export function start(shell) {
  // F1 to F5 switch screens wherever the focus is (UI-3).
  document.addEventListener('keydown', e => {
    if (!functionKey.test(e.key) || e.altKey || e.ctrlKey || e.shiftKey || e.metaKey) {
      return; // Alt+F4 isn't the Intake key
    }
    e.preventDefault(); // F5 never reloads, F3 never opens Find
    shell.invokeMethodAsync('OnFunctionKey', e.key);
  });

  document.addEventListener('keydown', keepTabInDialog);
  document.addEventListener('focusin', e => {
    if (e.target instanceof Element && !e.target.closest('.dialog')) {
      focusBeforeDialog = e.target;
    }
  });
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
