// The register's one JavaScript module: keyboard, focus and printing (UI-9).

const functionKey = /^F[1-5]$/;

// F1 to F5 switch screens wherever the focus is (UI-3).
export function start(shell) {
  document.addEventListener('keydown', e => {
    if (!functionKey.test(e.key) || e.altKey || e.ctrlKey || e.shiftKey || e.metaKey) {
      return; // Alt+F4 isn't the Intake key
    }
    e.preventDefault(); // F5 never reloads, F3 never opens Find
    shell.invokeMethodAsync('OnFunctionKey', e.key);
  });
}
