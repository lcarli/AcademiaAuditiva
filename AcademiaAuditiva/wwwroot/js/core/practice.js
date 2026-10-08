// Free practice (switch in Views/Exercise/_ExerciseHeader.cshtml). With the
// switch on, RequestPlay starts unscored rounds: ValidateExercise checks the
// answer but saves nothing, and RevealAnswer may show the answer first.
// A routine question (banner in Views/Exercise/_RoutineBanner.cshtml) is taken
// like a test instead: Play sends the routine, both calls return where the
// student stands in its "routine" field, and once the routine item takes no
// more answers Play says why without asking the server.
// The exercise scripts start and check rounds through play() and validate();
// this file keeps the counters, the "Show answer" button and the banners in step.
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

  function routineBanner() {
    return document.getElementById("aaRoutine");
  }

  // A failed response, shown by the exercise scripts through AAi18n.serverError,
  // while the routine item takes no more answers; null while it does.
  function routineBlock(banner) {
    const state = banner.dataset;
    return state.blockedTitle
      ? { success: false, title: state.blockedTitle, message: state.blockedMessage, icon: "info" }
      : null;
  }

  // Keeps the routine banner in step with the "routine" field of a response.
  function syncRoutine(data) {
    const banner = routineBanner();
    const status = data && data.routine;
    if (!banner || !status) return;

    banner.dataset.state = status.state;
    if (status.blocked) {
      banner.dataset.blockedTitle = status.blocked.title;
      banner.dataset.blockedMessage = status.blocked.message;
    } else {
      delete banner.dataset.blockedTitle;
      delete banner.dataset.blockedMessage;
    }
    // A refused question says why, rather than "Unable to validate".
    if (data.success === false) {
      data.title = data.title || (status.blocked && status.blocked.title);
      data.icon = "info";
    }

    const text = banner.querySelector("[data-aa-routine-text]");
    if (text) text.textContent = status.text;
    const verdict = banner.querySelector("[data-aa-routine-verdict]");
    if (verdict) {
      verdict.textContent = status.verdict || "";
      verdict.hidden = !status.verdict;
      verdict.classList.toggle("is-below", status.passed === false);
    }
    const late = banner.querySelector("[data-aa-routine-late]");
    if (late) late.hidden = status.state !== "late";
    const bar = banner.querySelector("[data-aa-routine-bar]");
    if (bar) {
      bar.style.width = `${status.percent}%`;
      bar.parentElement.setAttribute("aria-valuenow", String(status.percent));
    }
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

  // A game run (game.js) asks for its rounds through send(), so they stay scored.
  function play(body) {
    if (window.AAGame && window.AAGame.active()) return window.AAGame.play(body, (request) => send(body, request, false));

    const banner = routineBanner();
    const block = banner && routineBlock(banner);
    if (block) return Promise.resolve(block);

    const free = isFree();
    const request = banner
      ? Object.assign({}, body, {
          free: false,
          routineAssignmentId: Number(banner.dataset.assignmentId),
          routineItemId: Number(banner.dataset.itemId),
        })
      : Object.assign({}, body, { free });
    return send(body, request, free);
  }

  function send(body, request, free) {
    const input = toggle();
    // The switch waits for every round asked for (Play may be clicked again meanwhile),
    // so no round lands in the other mode.
    pending += 1;
    if (input) input.disabled = true;
    return post("/Exercise/RequestPlay", request)
      .then((data) => {
        syncRoutine(data);
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
    const request = post("/Exercise/ValidateExercise", body).then((data) => {
      syncRoutine(data);
      if (data && data.success !== false) {
        round = null;
        showReveal();
        if (data.free) bump(data.isCorrect ? "aaFreeCorrect" : "aaFreeWrong");
        else bump(data.isCorrect ? "correctCount" : "errorCount");
      }
      return data;
    });
    return window.AAGame && window.AAGame.active() ? window.AAGame.track(request) : request;
  }

  // Turning the switch drops the round on screen: the next Play starts one in the new mode.
  // A game run that ends drops it too (game.js).
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

  // The answer on a staff when the answer view draws one: { sheet, draw }, or null.
  // VexFlow measures the staff, so draw() once the sheet is on the page.
  function staffSheet(view, answer) {
    if (!view || !view.staff || !window.StaffRenderer) return null;
    const label = view.label || answerText(answer);
    const sheet = document.createElement("div");
    sheet.className = "aa-sheet aa-reveal-staff";
    sheet.setAttribute("role", "img");
    sheet.setAttribute("aria-label", label);
    return {
      sheet,
      draw() {
        try {
          window.StaffRenderer.render(sheet, Object.assign({}, view.staff, {
            width: Math.max(300, sheet.clientWidth - 24),
          }));
        } catch (err) {
          console.warn("Could not draw the answer:", err);
          sheet.textContent = label;
        }
      },
    };
  }

  function answerSheet(answer) {
    return staffSheet(answerView ? answerView(answer) : null, answer);
  }

  function showAnswer(answer, title) {
    const view = answerView ? answerView(answer) : null;
    const staff = staffSheet(view, answer);
    if (staff) return Swal.fire({ icon: "info", title, html: staff.sheet, width: "42em", didOpen: staff.draw });
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
    reset,
    staffNotes,
    answerSheet,
    decorate,
    onReset(handler) {
      if (typeof handler === "function") resetHandlers.push(handler);
    },
    // view(answer) returns the text to show, or { staff: StaffRenderer options, label: text read out }.
    setAnswerView(view) {
      answerView = typeof view === "function" ? view : null;
    },
  };

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
  else init();
})(window, document);
