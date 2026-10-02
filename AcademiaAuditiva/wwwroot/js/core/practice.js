// Free practice (switch in Views/Exercise/_ExerciseHeader.cshtml). With the
// switch on, RequestPlay starts unscored rounds: ValidateExercise checks the
// answer but saves nothing, and RevealAnswer may show the answer first.
// The exercise scripts start and check rounds through play() and validate();
// this file keeps the counters, the "Show answer" button and the banner in step.
(function (window, document) {
  "use strict";

  let round = null; // { exerciseId, roundId, free } of the round on screen
  let pending = 0; // RequestPlay calls still waiting for their round
  let answerView = null;
  const resetHandlers = [];

  function toggle() {
    return document.getElementById("aaFreePractice");
  }

  function isFree() {
    const input = toggle();
    return !!(input && input.checked);
  }

  function post(url, body) {
    return fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    }).then((resp) => resp.json());
  }

  function showReveal() {
    const button = document.querySelector("[data-aa-reveal]");
    if (button) button.hidden = !(round && round.free);
  }

  function bump(id) {
    const counter = document.getElementById(id);
    if (counter) counter.textContent = String((parseInt(counter.textContent, 10) || 0) + 1);
  }

  function play(body) {
    const input = toggle();
    const free = isFree();
    // The switch waits for every round asked for (Play may be clicked again meanwhile),
    // so no round lands in the other mode.
    pending += 1;
    if (input) input.disabled = true;
    return post("/Exercise/RequestPlay", Object.assign({}, body, { free }))
      .then((data) => {
        // An error keeps the round on screen, as the exercise scripts do.
        if (!(data && data.success === false)) {
          round = data && data.roundId ? { exerciseId: body.exerciseId, roundId: data.roundId, free } : null;
          showReveal();
        }
        return data;
      })
      .finally(() => {
        pending -= 1;
        if (input) input.disabled = pending > 0;
      });
  }

  // The server says whether the round was free; only then does the banner tally count it.
  function validate(body) {
    return post("/Exercise/ValidateExercise", body).then((data) => {
      if (data && data.success !== false) {
        round = null;
        showReveal();
        if (data.free) bump(data.isCorrect ? "aaFreeCorrect" : "aaFreeWrong");
        else bump(data.isCorrect ? "correctCount" : "errorCount");
      }
      return data;
    });
  }

  // Turning the switch drops the round on screen: the next Play starts one in the new mode.
  function reset() {
    round = null;
    showReveal();
    resetHandlers.forEach((handler) => {
      try {
        handler();
      } catch (err) {
        console.error("Could not reset the exercise:", err);
      }
    });
  }

  // "E4:q|bar|rest:hr" as StaffRenderer notes. Rhythm answers hold durations
  // only ("q|bar|hr"), drawn on `pitch`.
  function staffNotes(answer, pitch) {
    return String(answer || "")
      .split("|")
      .map((token) => token.trim())
      .filter(Boolean)
      .map((token) => {
        if (token === "bar" || token === "barline") return { note: "barline" };
        const colon = token.indexOf(":");
        const note = colon >= 0 ? token.slice(0, colon) : pitch;
        const duration = colon >= 0 ? token.slice(colon + 1) : token;
        if (note === "rest" || duration.endsWith("r")) {
          return { note: "rest", duration: duration.endsWith("r") ? duration : duration + "r" };
        }
        return { note, duration };
      });
  }

  function answerText(answer) {
    return window.AAi18n ? window.AAi18n.answerLabel(answer) : String(answer || "");
  }

  function showAnswer(answer, title) {
    const view = answerView ? answerView(answer) : null;
    if (view && view.staff && window.StaffRenderer) {
      const sheet = document.createElement("div");
      sheet.className = "aa-sheet aa-reveal-staff";
      sheet.setAttribute("role", "img");
      sheet.setAttribute("aria-label", answerText(answer));
      return Swal.fire({
        icon: "info",
        title,
        html: sheet,
        width: "42em",
        // VexFlow measures the staff, so draw it once the dialog is on the page.
        didOpen: () => {
          try {
            window.StaffRenderer.render(sheet, Object.assign({}, view.staff, {
              width: Math.max(300, sheet.clientWidth - 24),
            }));
          } catch (err) {
            console.warn("Could not draw the answer:", err);
            sheet.textContent = answerText(answer);
          }
        },
      });
    }
    return Swal.fire({ icon: "info", title, text: typeof view === "string" ? view : answerText(answer) });
  }

  function reveal(button) {
    const current = round;
    if (!current || !current.free) return;
    const loc = window.AAi18n ? window.AAi18n.localizer() : {};
    const title = document.getElementById("aaFreeNote")?.dataset.revealTitle || "";
    button.disabled = true;
    post("/Exercise/RevealAnswer", { exerciseId: current.exerciseId, roundId: current.roundId })
      .then((data) => {
        if (window.AAi18n && window.AAi18n.serverError(data, loc)) return;
        return showAnswer(data.answer, title);
      })
      .catch((err) => {
        console.error("Could not show the answer:", err);
        Swal.fire({ icon: "error", title: loc.validationErrorTitle, text: loc.validationErrorText });
      })
      .finally(() => {
        button.disabled = false;
      });
  }

  // Answer dialog footer of a free round, in place of the XP (see AAi18n.withRewards).
  function decorate(options) {
    const text = document.getElementById("aaFreeNote")?.dataset.resultNote;
    if (!text) return options;
    const footer = document.createElement("div");
    footer.className = "aa-free-footer";
    const icon = document.createElement("i");
    icon.className = "bi bi-headphones";
    icon.setAttribute("aria-hidden", "true");
    const span = document.createElement("span");
    span.textContent = text;
    footer.append(icon, span);
    options.footer = footer;
    return options;
  }

  function syncBanner() {
    const free = isFree();
    const note = document.getElementById("aaFreeNote");
    if (note) note.hidden = !free;
    document.querySelector(".aa-exercise-head")?.classList.toggle("is-free", free);
  }

  // ?practice=free keeps the mode on reload and in shared links.
  function remember() {
    const url = new URL(window.location.href);
    if (isFree()) url.searchParams.set("practice", "free");
    else url.searchParams.delete("practice");
    window.history.replaceState(window.history.state, "", url);
  }

  function init() {
    const input = toggle();
    if (!input) return;

    const template = document.getElementById("aaRevealTemplate");
    const check = document.getElementById("validateGuess");
    if (template && template.content.firstElementChild && check) {
      const button = document.importNode(template.content.firstElementChild, true);
      check.after(button);
      button.addEventListener("click", () => reveal(button));
    }

    syncBanner();
    const inUrl = (new URLSearchParams(window.location.search).get("practice") || "").toLowerCase() === "free";
    if (inUrl !== input.checked) remember();

    input.addEventListener("change", () => {
      syncBanner();
      remember();
      reset();
    });
  }

  window.AAPractice = {
    isFree,
    play,
    validate,
    staffNotes,
    decorate,
    onReset(handler) {
      if (typeof handler === "function") resetHandlers.push(handler);
    },
    // view(answer) returns the text to show, or { staff: StaffRenderer options }.
    setAnswerView(view) {
      answerView = typeof view === "function" ? view : null;
    },
  };

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
  else init();
})(window, document);
