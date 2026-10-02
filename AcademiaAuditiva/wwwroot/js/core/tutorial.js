// Guided tours. The first time a user opens a page that has one, the tour walks
// them through it one highlighted element at a time. TutorialViewComponent
// renders the localized steps into #aa-tour-data; closing the tour, finished or
// skipped, tells TutorialController so it does not start on its own again.
// Buttons marked with data-aa-tour-start replay it.
(function (window, document) {
  "use strict";

  const MARGIN = 12; // between the popover and the edges of the screen
  const GAP = 12; // between the highlight and the popover
  const PAD = 8; // between the elements and the edge of their highlight

  const source = document.getElementById("aa-tour-data");
  if (!source || window.AATour) return;

  let tour;
  try {
    tour = JSON.parse(source.textContent);
  } catch (err) {
    console.error("Could not read the guided tour:", err);
    return;
  }
  if (!tour || !Array.isArray(tour.steps) || !tour.steps.length) return;

  const labels = tour.labels || {};
  const reducedMotion = window.matchMedia ? window.matchMedia("(prefers-reduced-motion: reduce)") : null;
  let active = null;

  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
  }

  function button(className, text) {
    const node = el("button", className, text);
    node.type = "button";
    return node;
  }

  function format(template, values) {
    return String(template || "").replace(/\{(\d+)\}/g, (match, index) =>
      Number(index) < values.length ? String(values[Number(index)]) : match);
  }

  function isVisible(node) {
    if (!node.getClientRects().length) return false;
    const rect = node.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0 && window.getComputedStyle(node).visibility !== "hidden";
  }

  // The visible elements a step points at. A step can name fallbacks, like the
  // menu button for links that a small screen folds into the menu.
  function targetsOf(step) {
    if (!step.target) return [];
    try {
      return Array.from(document.querySelectorAll(step.target)).filter(isVisible);
    } catch (err) {
      console.error("Invalid guided tour target:", step.target, err);
      return [];
    }
  }

  // One rectangle around all the elements, with room for the highlight's padding.
  function spotRect(nodes) {
    const rects = nodes.map((node) => node.getBoundingClientRect());
    const top = Math.min(...rects.map((r) => r.top)) - PAD;
    const left = Math.min(...rects.map((r) => r.left)) - PAD;
    const right = Math.max(...rects.map((r) => r.right)) + PAD;
    const bottom = Math.max(...rects.map((r) => r.bottom)) + PAD;
    return { top, left, right, bottom, width: right - left, height: bottom - top };
  }

  function viewport() {
    return { width: document.documentElement.clientWidth, height: window.innerHeight };
  }

  // Scrolls the step's elements on screen, with the popover too when both fit.
  // Nothing moves when they are already in view.
  function reveal(run, nodes) {
    if (!nodes.length) return;
    const view = viewport();
    const spot = spotRect(nodes);
    const popHeight = run.pop.offsetHeight;
    const onScreen = spot.top >= 0 && spot.bottom <= view.height;
    const popFits = spot.bottom + GAP + popHeight <= view.height - MARGIN || spot.top - GAP - popHeight >= MARGIN;
    if (onScreen && popFits) return;

    const room = view.height - 2 * MARGIN;
    const both = spot.height + GAP + popHeight;
    let delta;
    if (both <= room) delta = spot.top - (view.height - both) / 2;
    else if (onScreen) return;
    else if (spot.height <= room) delta = spot.top - (view.height - spot.height) / 2;
    else delta = spot.top - MARGIN;

    window.scrollBy({ top: delta, behavior: reducedMotion && reducedMotion.matches ? "auto" : "smooth" });
  }

  // Frames the step's elements and puts the popover below them, above them, or
  // at the bottom of the screen, whichever fits first. A step without visible
  // elements is shown in the middle of the screen.
  function place(run) {
    run.frame = 0;
    if (active !== run) return;
    const view = viewport();
    const pop = run.pop;
    const width = pop.offsetWidth;
    const height = pop.offsetHeight;
    const nodes = targetsOf(run.steps[run.index]);
    run.root.classList.toggle("is-centered", !nodes.length);

    if (!nodes.length) {
      pop.style.left = Math.max(MARGIN, (view.width - width) / 2) + "px";
      pop.style.top = Math.max(MARGIN, (view.height - height) / 2) + "px";
      return;
    }

    const spot = spotRect(nodes);
    Object.assign(run.spot.style, {
      top: spot.top + "px",
      left: spot.left + "px",
      width: spot.width + "px",
      height: spot.height + "px"
    });

    let top = view.height - MARGIN - height;
    if (spot.bottom + GAP + height <= view.height - MARGIN) top = spot.bottom + GAP;
    else if (spot.top - GAP - height >= MARGIN) top = spot.top - GAP - height;
    const left = Math.min(spot.left + spot.width / 2 - width / 2, view.width - MARGIN - width);
    pop.style.top = Math.max(MARGIN, top) + "px";
    pop.style.left = Math.max(MARGIN, left) + "px";
  }

  function show(run, index) {
    const step = run.steps[index];
    const last = index === run.steps.length - 1;
    run.index = index;
    run.count.textContent = format(labels.stepOf, [index + 1, run.steps.length]);
    run.title.textContent = step.title || "";
    run.text.textContent = step.text || "";
    run.back.hidden = index === 0;
    run.skip.hidden = last;
    run.next.textContent = last ? labels.done : labels.next;

    const nodes = targetsOf(step);
    if (run.observer) {
      // The body catches layout shifts around the elements, the popover its own size.
      run.observer.disconnect();
      [document.body, run.pop].concat(nodes).forEach((node) => run.observer.observe(node));
    }
    reveal(run, nodes);
    place(run);
    run.next.focus({ preventScroll: true });
  }

  function forward(run) {
    if (run.index < run.steps.length - 1) show(run, run.index + 1);
    else close(run, true);
  }

  // Keeps Tab on the popover's buttons; returns whether it moved the focus itself.
  function trapFocus(run, backwards) {
    const items = [run.skip, run.back, run.next].filter((node) => !node.hidden);
    const first = items[0];
    const last = items[items.length - 1];
    const current = document.activeElement;
    if (!items.includes(current)) (backwards ? last : first).focus();
    else if (backwards && current === first) last.focus();
    else if (!backwards && current === last) first.focus();
    else return false;
    return true;
  }

  function onKeyDown(event) {
    const run = active;
    if (!run) return;
    const plain = !event.altKey && !event.ctrlKey && !event.metaKey;
    let handled = plain;
    if (plain && event.key === "Escape") close(run, false);
    else if (plain && event.key === "ArrowRight" && run.index < run.steps.length - 1) show(run, run.index + 1);
    else if (plain && event.key === "ArrowLeft" && run.index > 0) show(run, run.index - 1);
    else if (event.key === "Tab") handled = trapFocus(run, event.shiftKey);
    else handled = false;
    if (handled) {
      event.preventDefault();
      event.stopPropagation();
    }
  }

  function close(run, finished) {
    if (active !== run) return;
    active = null;
    document.removeEventListener("keydown", onKeyDown, true);
    document.removeEventListener("scroll", run.schedule, true);
    window.removeEventListener("resize", run.schedule);
    if (run.observer) run.observer.disconnect();
    if (run.frame) window.cancelAnimationFrame(run.frame);
    run.root.remove();
    run.inert.forEach((node) => { node.inert = false; });
    const back = run.returnFocus;
    if (back && back !== document.body && back.isConnected && typeof back.focus === "function") {
      back.focus({ preventScroll: true });
    }

    if (tour.url) {
      // keepalive lets the request finish when the user leaves the page right away.
      fetch(tour.url, {
        method: "POST",
        body: new URLSearchParams({ key: tour.key, finished: String(finished) }),
        credentials: "same-origin",
        keepalive: true
      }).catch(() => {});
    }
  }

  function start() {
    if (active) return;
    // Steps whose elements this page or screen size doesn't show are left out.
    const steps = tour.steps.filter((step) => !step.target || targetsOf(step).length);
    if (!steps.length) return;

    const run = { steps, index: 0, frame: 0, inert: [], returnFocus: document.activeElement };
    run.root = el("div", "aa-tour");
    run.spot = el("div", "aa-tour-spot");
    run.spot.setAttribute("aria-hidden", "true");
    run.pop = el("div", "aa-tour-pop");
    // A click on the popover's text keeps the focus in the dialog.
    run.pop.tabIndex = -1;
    run.pop.setAttribute("role", "dialog");
    run.pop.setAttribute("aria-modal", "true");
    run.pop.setAttribute("aria-labelledby", "aa-tour-title");
    run.pop.setAttribute("aria-describedby", "aa-tour-text");
    run.count = el("p", "aa-tour-count");
    run.title = el("h2", "aa-tour-title");
    run.title.id = "aa-tour-title";
    run.text = el("p", "aa-tour-text");
    run.text.id = "aa-tour-text";
    run.live = el("div");
    run.live.append(run.count, run.title, run.text);
    run.skip = button("btn btn-link btn-sm aa-tour-skip", labels.skip);
    run.back = button("btn btn-outline-secondary btn-sm", labels.back);
    run.next = button("btn btn-primary btn-sm", labels.next);
    const nav = el("div", "aa-tour-nav");
    nav.append(run.back, run.next);
    const actions = el("div", "aa-tour-actions");
    actions.append(run.skip, nav);
    run.pop.append(run.live, actions);
    run.root.append(run.spot, run.pop);

    // A click on the dimmed page does nothing, and leaves the focus where it is.
    run.root.addEventListener("mousedown", (event) => {
      if (event.target === run.root) event.preventDefault();
    });
    run.skip.addEventListener("click", () => close(run, false));
    run.back.addEventListener("click", () => show(run, run.index - 1));
    run.next.addEventListener("click", () => forward(run));
    run.schedule = () => {
      if (!run.frame) run.frame = window.requestAnimationFrame(() => place(run));
    };

    // The rest of the page stays out of reach, for the mouse, the keyboard and
    // screen readers, until the tour closes.
    Array.from(document.body.children).forEach((node) => {
      if (node.tagName !== "SCRIPT" && !node.inert) {
        node.inert = true;
        run.inert.push(node);
      }
    });
    document.body.append(run.root);
    active = run;

    document.addEventListener("keydown", onKeyDown, true);
    document.addEventListener("scroll", run.schedule, true);
    window.addEventListener("resize", run.schedule);
    if ("ResizeObserver" in window) run.observer = new ResizeObserver(run.schedule);

    show(run, 0);
    // Announces the next steps; the first one is read as the dialog opens.
    run.live.setAttribute("aria-live", "polite");
  }

  window.AATour = { start };

  document.querySelectorAll("[data-aa-tour-start]").forEach((node) => {
    node.hidden = false;
    node.addEventListener("click", () => start());
  });

  if (tour.autoStart === true) {
    // Waits for images and fonts, so the highlight lands where the elements
    // end up, and leaves the page alone when a dialog is already open.
    const autoStart = () => {
      if (!document.querySelector(".modal.show, .swal2-container")) start();
    };
    if (document.readyState === "complete") autoStart();
    else window.addEventListener("load", autoStart, { once: true });
  }
})(window, document);
