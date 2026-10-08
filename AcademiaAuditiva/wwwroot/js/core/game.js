// Game modes: the bar in Views/Exercise/_GameBar.cshtml (the hub at /Games links to it).
// The exercise page plays as usual; this file starts a run on the first Play (the placement
// test's run is started by the hub), sends it with every round through AAPractice.play, keeps
// the bar in step with the "game" status the server returns, and shows each answer briefly so
// the next round follows: a right answer closes by itself, a wrong one after a moment. The
// sprint's clock runs here, but the server decides which answers count (GameRules).
(function (window, document) {
  "use strict";

  const CORRECT_MS = 900;
  const WRONG_MS = 2500;

  let bar = null;
  let state = "idle"; // idle → starting → running → ending → over; Play in idle or over starts a run
  let runId = null;
  let deadline = 0; // the sprint's end, in performance.now() time
  let ticker = null;
  let inflight = 0; // answers sent and not back yet
  let dialogOpen = false;
  let roundOpen = false; // a round was asked and not answered yet
  let finishing = false;
  let celebrations = []; // [rewards, path] pairs, celebrated once the run is over
  let locked = [];

  const never = () => new Promise(() => {});

  function find() {
    if (!bar) bar = document.getElementById("aaGame");
    return bar;
  }

  function data() {
    return bar.dataset;
  }

  function placement() {
    return data().mode === "placement";
  }

  function currentRun() {
    return placement() ? Number(data().run) || null : runId;
  }

  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
  }

  function post(url, body) {
    return fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    }).then((resp) => {
      if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
      return resp.json();
    });
  }

  function setText(text) {
    const node = bar.querySelector("[data-aa-game-text]");
    if (node && text) node.textContent = text;
  }

  function showClock(seconds) {
    const node = bar.querySelector("[data-aa-game-timer]");
    if (!node) return;
    const left = Math.max(0, seconds);
    node.textContent = `${Math.floor(left / 60)}:${String(left % 60).padStart(2, "0")}`;
    node.classList.toggle("is-low", state === "running" && left <= 10);
  }

  function stopClock() {
    if (ticker) window.clearInterval(ticker);
    ticker = null;
  }

  function startClock(seconds) {
    stopClock();
    deadline = performance.now() + seconds * 1000;
    const tick = () => {
      const left = Math.ceil((deadline - performance.now()) / 1000);
      showClock(left);
      if (left <= 0) {
        stopClock();
        timeUp();
      }
    };
    tick();
    ticker = window.setInterval(tick, 250);
  }

  function showStop(show) {
    const button = bar.querySelector("[data-aa-game-stop]");
    if (button) button.hidden = !show;
  }

  // A run plays with the settings it started with (RequestPlay enforces them), so they stay put.
  function lockFilters(lock) {
    if (lock) {
      locked = Array.from(document.querySelectorAll("#filtersModal select")).filter((select) => !select.disabled);
      locked.forEach((select) => {
        select.disabled = true;
      });
    } else {
      locked.forEach((select) => {
        select.disabled = false;
      });
      locked = [];
    }
  }

  function start(body) {
    state = "starting";
    return post(data().startUrl, {
      mode: data().mode,
      exerciseId: Number(body.exerciseId),
      filters: body.filters || null,
    })
      .then((resp) => {
        const game = resp && resp.game;
        if (!game) throw new Error("The game did not start.");
        runId = game.runId;
        state = "running";
        finishing = false;
        roundOpen = false;
        celebrations = [];
        setText(game.text);
        lockFilters(true);
        showStop(true);
        if (game.secondsLeft != null) startClock(game.secondsLeft);
        return runId;
      })
      .catch((err) => {
        console.error("Could not start the game:", err);
        state = "idle";
        Swal.fire({ icon: "error", title: data().errorTitle, text: data().errorText });
        return null;
      });
  }

  function nextRound() {
    const button = document.getElementById("Play");
    if (button && !button.disabled) button.click();
  }

  // Everything the run unlocked, one dialog after another, then what the player picked.
  function celebrateQueued() {
    const queued = celebrations;
    celebrations = [];
    if (!window.AARewards) return Promise.resolve();
    return queued.reduce(
      (chain, [rewards, path]) => chain.then(() => window.AARewards.celebrate(rewards, path)),
      Promise.resolve());
  }

  function endDialog(game) {
    const end = game.end || {};
    const content = el("div", "aa-game-end");
    content.append(el("p", "aa-game-end-score", String(game.score)));
    if (end.text) content.append(el("p", "aa-game-end-text", end.text));
    if (end.record) content.append(el("p", "aa-game-end-record" + (end.newBest ? " is-new" : ""), end.record));

    return Swal.fire({
      icon: end.newBest ? "success" : "info",
      title: end.title || "",
      html: content,
      showCancelButton: true,
      confirmButtonText: data().againText,
      cancelButtonText: data().hubText,
      customClass: { popup: "aa-game-end-popup" },
    }).then((result) =>
      celebrateQueued().then(() => {
        if (result.isConfirmed) nextRound();
        else if (result.dismiss === Swal.DismissReason.cancel) window.location.assign(data().hubUrl);
      }));
  }

  function over(game) {
    if (state === "over") return;
    state = "over";
    finishing = false;
    roundOpen = false;
    stopClock();
    showClock(game && game.secondsLeft != null ? game.secondsLeft : 0);
    showStop(false);
    lockFilters(false);
    // The round on screen, if any, no longer belongs to the run.
    if (window.AAPractice && window.AAPractice.reset) window.AAPractice.reset();
    if (!game) {
      setText(data().readyText);
      return;
    }
    setText(game.text);
    if (placement()) {
      if (game.resultUrl) window.location.assign(game.resultUrl);
      return;
    }
    endDialog(game);
  }

  // Ends the run once the answers on their way are back and their dialogs closed.
  function maybeFinish() {
    if (state !== "ending" || finishing || inflight > 0 || dialogOpen) return;
    finishing = true;
    post(data().finishUrl, { runId })
      .then((resp) => over(resp && resp.game))
      .catch((err) => {
        console.error("Could not finish the game:", err);
        over(null);
      });
  }

  function timeUp() {
    if (state !== "running") return;
    state = "ending";
    showStop(false);
    maybeFinish();
  }

  function afterAnswer(game) {
    if (game.over) over(game);
    else if (game.nextUrl) window.location.assign(game.nextUrl);
    else if (state === "ending") maybeFinish();
    else if (state === "running") nextRound();
  }

  // AAPractice.play hands the round over: `send` asks RequestPlay for it.
  function play(body, send) {
    const block = data().blockedTitle;
    if (block) return Promise.resolve({ success: false, title: block, message: data().blockedMessage, icon: "info" });
    if (state === "starting" || state === "ending") return never();
    if (state === "running" && roundOpen) {
      Swal.fire({ icon: "info", title: data().busyTitle, text: data().busyText });
      return never();
    }

    let ready;
    if (placement()) {
      if (state === "over") return never();
      state = "running";
      ready = Promise.resolve(currentRun());
    } else {
      ready = state === "running" ? Promise.resolve(runId) : start(body);
    }

    return ready.then((id) => {
      if (!id) return never();
      return send(Object.assign({}, body, { free: false, gameRunId: id })).then((resp) => {
        const game = resp && resp.game;
        if (resp && resp.success === false && game) {
          // The run is over (the sprint's time ran out) or the placement test moved on.
          if (game.over) {
            over(game);
            return never();
          }
          if (game.nextUrl) {
            window.location.assign(game.nextUrl);
            return never();
          }
        }
        if (resp && resp.roundId) roundOpen = true;
        if (game) setText(game.text);
        return resp;
      });
    });
  }

  // AAPractice.validate reports each answer, so the run ends only once they are all back.
  function track(request) {
    inflight += 1;
    return request
      .then((resp) => {
        roundOpen = false;
        return resp;
      }, (err) => {
        roundOpen = false;
        throw err;
      })
      .finally(() => {
        inflight -= 1;
        // After the exercise script has opened the answer dialog.
        window.setTimeout(maybeFinish, 0);
      });
  }

  // Whether AAi18n.result shows the answer as part of the run.
  function shows(resp) {
    const game = resp && resp.game;
    return !!(find() && game && (state === "running" || state === "ending") && game.runId === currentRun());
  }

  function show(options, resp) {
    const game = resp.game;
    setText(game.text);
    const last = game.over || state === "ending";
    if (resp.isCorrect) {
      Object.assign(options, { timer: CORRECT_MS, timerProgressBar: true, showConfirmButton: false });
    } else if (last) {
      options.confirmButtonText = data().resultText;
    } else {
      Object.assign(options, { timer: WRONG_MS, timerProgressBar: true, confirmButtonText: data().nextText });
    }

    dialogOpen = true;
    return Swal.fire(options).then((result) => {
      dialogOpen = false;
      // The placement test celebrates between questions; the other modes once the run is over.
      const celebrated = placement() ? celebrateQueued() : Promise.resolve();
      celebrated.then(() => afterAnswer(game));
      return result;
    });
  }

  // AARewards.decorate keeps the level ups and badges of a run's answers for later (show, endDialog).
  function defer(rewards, path, resp) {
    if (!shows(resp)) return false;
    celebrations.push([rewards, path]);
    return true;
  }

  function init() {
    if (!find()) return;
    // The hub started the placement test's run; the page plays its current question.
    if (placement() && currentRun() && !data().blockedTitle) state = "running";
    const stop = bar.querySelector("[data-aa-game-stop]");
    if (stop) stop.addEventListener("click", timeUp);
  }

  window.AAGame = {
    active: () => !!find(),
    play,
    track,
    shows,
    show,
    defer,
  };

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
  else init();
})(window, document);
