// Closes success alerts marked with data-aa-autodismiss="<ms>" after the delay.
// The countdown pauses while the alert is hovered or has focus, and error
// alerts are never auto-dismissed.
(function (window, document) {
  function arm(el) {
    const delay = parseInt(el.dataset.aaAutodismiss, 10);
    if (!delay || !window.bootstrap || !window.bootstrap.Alert) return;

    let timer = 0;
    const close = () => window.bootstrap.Alert.getOrCreateInstance(el).close();
    const start = () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(close, delay);
    };
    const stop = () => window.clearTimeout(timer);

    el.addEventListener("mouseenter", stop);
    el.addEventListener("focusin", stop);
    el.addEventListener("mouseleave", () => {
      if (!el.contains(document.activeElement)) start();
    });
    el.addEventListener("focusout", (event) => {
      if (!el.contains(event.relatedTarget) && !el.matches(":hover")) start();
    });
    start();
  }

  function init() {
    document.querySelectorAll("[data-aa-autodismiss]").forEach(arm);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})(window, document);
