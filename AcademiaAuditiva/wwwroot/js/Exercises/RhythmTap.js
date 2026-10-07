// RhythmTap: a round plays a count-in, a two-bar rhythm and the count-in again; then the
// student taps the rhythm back on the pad or with the space bar, one tap where each note
// starts. The page sends when each tap came, in milliseconds from the first, and the server
// matches them to the notes at the tempo and from the moment that fit them best (RhythmTaps).
document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = AAi18n.localizer();
  const exerciseId = document.getElementById("exerciseId")?.value;
  const levelSelect = document.getElementById("rtLevel");
  const tempoSelect = document.getElementById("rtTempo");
  const pad = document.getElementById("tapPad");
  const status = document.getElementById("tapStatus");
  const count = document.getElementById("tapCount");
  const clearButton = document.getElementById("clearTaps");
  // As many as the server reads (RhythmTaps.MostTaps).
  const MOST_TAPS = 64;

  let playToken = null;
  let roundId = null;
  let timeSignature = "4/4";
  // How far into the round's clip the taps start counting, in seconds (metadata.tapsFrom).
  let tapsFrom = 0;
  let taps = [];
  // idle: no round, or one answered; listening: its clip plays; armed: the taps count.
  let state = "idle";
  // Each Play and Replay listens anew; what an earlier listen left waiting does nothing.
  let listening = 0;
  let armTimer = 0;
  let hitTimer = 0;
  // How the last taps went (RhythmTaps.Result), to mark in red the notes tapped off time.
  let lastDetail = null;

  function format(template, values) {
    return String(template || "").replace(/\{\{(\d+)\}\}/g, (_, index) => values[Number(index)] ?? "");
  }

  function showTaps() {
    count.textContent = format(loc.tapCount, [taps.length]);
    clearButton.disabled = taps.length === 0;
  }

  function setState(next) {
    state = next;
    const armed = next === "armed";
    pad.disabled = !armed;
    pad.classList.toggle("is-armed", armed);
    status.textContent = armed ? loc.tapNow : next === "listening" ? loc.tapListen : loc.tapPlaceholder;
  }

  function stopListening() {
    listening += 1;
    clearTimeout(armTimer);
  }

  // Plays the round's clip and takes the taps from tapsFrom seconds into it, by the clock of
  // the audio the student hears: the clicks of the count-in are what the taps follow.
  function listen() {
    stopListening();
    const mine = listening;
    taps = [];
    showTaps();
    setState("listening");
    const arm = () => {
      if (mine === listening && state === "listening") setState("armed");
    };
    AudioEngine.playToken(playToken, {
      onStart(clock) {
        const wait = () => {
          if (mine !== listening) return;
          const left = tapsFrom - clock();
          if (left <= 0) arm();
          else armTimer = setTimeout(wait, Math.min(250, Math.max(15, left * 1000)));
        };
        wait();
      },
    })
      // By the end of the clip the taps are due, should the audio clock have stalled.
      .then(arm)
      .catch((err) => {
        console.error("Could not play the rhythm:", err);
        if (mine === listening) setState("idle");
      });
  }

  // When the input came, on the clock of performance.now(), which event.timeStamp shares in
  // every current browser: the handler itself may run late, while the page is busy.
  function timeOf(event) {
    const now = performance.now();
    const stamp = event.timeStamp;
    return Number.isFinite(stamp) && stamp > 0 && stamp <= now ? stamp : now;
  }

  function tap(event) {
    if (state !== "armed" || taps.length >= MOST_TAPS) return;
    const last = taps.length ? taps[taps.length - 1] : 0;
    taps.push(Math.max(last, timeOf(event)));
    showTaps();
    pad.classList.add("is-hit");
    clearTimeout(hitTimer);
    hitTimer = setTimeout(() => pad.classList.remove("is-hit"), 90);
  }

  // A tap counts as the finger or the button goes down, so no click: that comes on release.
  pad.addEventListener("pointerdown", (event) => {
    if (event.pointerType === "mouse" && event.button !== 0) return;
    event.preventDefault();
    tap(event);
  });

  // Where a key types or picks, and in a dialog, the space bar keeps its job.
  function keepsKeys(event) {
    const target = event.target;
    const typing = target instanceof Element
      && (target.isContentEditable || target.closest("input, textarea, select, [contenteditable]"));
    const body = document.body.classList;
    return typing || body.contains("modal-open") || body.contains("swal2-shown");
  }

  function isSpace(event) {
    return event.code === "Space" || event.key === " ";
  }

  // While a round plays or waits for its taps, the space bar taps, and clicks no button the
  // focus is on (Play would start another round); Enter taps on the pad.
  document.addEventListener("keydown", (event) => {
    if (state === "idle" || keepsKeys(event)) return;
    if (!isSpace(event) && !(event.key === "Enter" && event.target === pad)) return;
    event.preventDefault();
    if (!event.repeat) tap(event);
  });

  document.addEventListener("keyup", (event) => {
    if (state !== "idle" && !keepsKeys(event) && isSpace(event)) event.preventDefault();
  });

  clearButton.addEventListener("click", () => {
    taps = [];
    showTaps();
  });

  // Under a wrong answer: how many notes the rhythm has, when the taps were not as many;
  // otherwise how many notes were tapped off time, which the staff shows in red.
  function caption(detail) {
    if (!detail) return "";
    if (!Array.isArray(detail.deviations)) return format(loc.tapCountCaption, [detail.taps, detail.notes]);
    const off = detail.deviations.filter((deviation) => Math.abs(deviation) > detail.tolerance).length;
    return format(loc.tapOffCaption, [off, detail.notes]);
  }

  // The notes of the rhythm come in the order the server scored them, rests and barlines aside.
  function marked(notes) {
    const deviations = lastDetail && Array.isArray(lastDetail.deviations) ? lastDetail.deviations : null;
    if (!deviations) return notes;
    let index = 0;
    return notes.map((note) => {
      if (note.note === "barline" || note.note === "rest") return note;
      const off = Math.abs(deviations[index++] ?? 0) > lastDetail.tolerance;
      return off ? Object.assign({}, note, { selected: true }) : note;
    });
  }

  // A wrong answer and "Show answer" draw the rhythm played on a staff.
  AAPractice.setAnswerView((answer) => {
    const notes = marked(AAPractice.staffNotes(answer, "B4"));
    return {
      staff: { rhythm: true, timeSignature, notes },
      label: AAStaffEntryExercise.spokenRhythm(notes),
    };
  });

  // Turning free practice on or off drops the round on screen.
  AAPractice.onReset(() => {
    stopListening();
    playToken = null;
    roundId = null;
    lastDetail = null;
    taps = [];
    showTaps();
    setState("idle");
  });

  document.getElementById("Play")?.addEventListener("click", () => {
    stopListening();
    lastDetail = null;
    setState("listening");
    AAPractice.play({
      exerciseId,
      filters: { rtLevel: levelSelect?.value || "1", rtTempo: tempoSelect?.value || "120" },
    })
      .then((data) => {
        if (AAi18n.serverError(data, loc)) {
          setState("idle");
          return;
        }
        playToken = data.playToken;
        roundId = data.roundId;
        const metadata = data.metadata || {};
        timeSignature = metadata.timeSignature || "4/4";
        tapsFrom = Number(metadata.tapsFrom) || 0;
        if (playToken) listen();
        else setState("idle");
      })
      .catch((err) => {
        console.error("Could not start a round:", err);
        setState("idle");
      });
  });

  document.getElementById("Replay")?.addEventListener("click", () => {
    if (!playToken) {
      AAi18n.noAudio(loc);
      return;
    }
    listen();
  });

  document.getElementById("validateGuess")?.addEventListener("click", () => {
    if (!roundId) {
      AAi18n.noAudio(loc);
      return;
    }
    if (!taps.length) {
      AAi18n.incomplete(loc);
      return;
    }
    const first = taps[0];
    AAPractice.validate({
      exerciseId,
      roundId,
      userGuess: taps.map((time) => Math.round(time - first)).join(","),
    }).then((data) => {
      if (AAi18n.serverError(data, loc)) return;
      stopListening();
      setState("idle");
      lastDetail = data.detail || null;
      data.caption = caption(lastDetail);
      AAi18n.result(data, loc);
      playToken = null;
      roundId = null;
    });
  });

  showTaps();
  setState("idle");
});
