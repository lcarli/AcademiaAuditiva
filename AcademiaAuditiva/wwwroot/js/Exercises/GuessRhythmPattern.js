// GuessRhythmPattern: a round plays a rhythm and offers four written on one-line staves
// (metadata.options), one of them the rhythm played. They show once Play is clicked.
document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = AAi18n.localizer();
  const exerciseId = document.getElementById("exerciseId")?.value;
  const levelSelect = document.getElementById("grpLevel");
  const tempoSelect = document.getElementById("grpTempo");
  const list = document.getElementById("rhythmOptions");
  const placeholder = document.getElementById("rhythmPlaceholder");
  let playToken = null;
  let roundId = null;
  let timeSignature = "4/4";
  let selected = "";

  function format(template, values) {
    return String(template || "").replace(/\{\{(\d+)\}\}/g, (_, index) => values[Number(index)] ?? "");
  }

  function notesOf(rhythm) {
    return AAPractice.staffNotes(rhythm, "B4");
  }

  function optionButtons() {
    return Array.from(list.querySelectorAll(".aa-rhythm-option"));
  }

  // Below this scale a staff's notes are too small to read, so each bar takes its own line.
  const MIN_SCALE = 0.6;
  let drawnFor = 0;

  function staffOptions(rhythm, width, withTime) {
    return {
      rhythm: true,
      timeSignature,
      hideTimeSignature: !withTime,
      notes: notesOf(rhythm),
      width,
      noteWidth: 20,
      crop: true,
    };
  }

  function contentWidth(element) {
    const style = getComputedStyle(element);
    return element.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
  }

  // Draws every part at one width, as wide as the widest needs, so their notes share one size.
  function drawParts(parts, width) {
    const draw = (part, w) => StaffRenderer.render(part.element, staffOptions(part.rhythm, w, part.withTime)).width;
    const widths = parts.map((part) => draw(part, width));
    const widest = Math.max(...widths);
    if (widths.some((w) => w !== widest)) parts.forEach((part) => draw(part, widest));
    return widest;
  }

  // Each rhythm takes one line, or one line per bar where a line would shrink its notes too
  // much (sixteenths on a phone); then each line after the first keeps the clef but not the meter.
  function drawStaves() {
    const staves = Array.from(list.querySelectorAll(".aa-rhythm-staff"));
    drawnFor = list.clientWidth;
    if (!staves.length) return;
    const inner = Math.floor(contentWidth(staves[0]));
    const width = Math.max(240, inner);
    const lines = staves.map((staff) => ({ element: staff, rhythm: staff.dataset.rhythm, withTime: true }));
    if (drawParts(lines, width) * MIN_SCALE <= inner) return;
    const bars = staves.flatMap((staff) => {
      staff.replaceChildren();
      return staff.dataset.rhythm.split("|bar|").map((rhythm, index) => {
        const element = document.createElement("span");
        element.className = "aa-rhythm-bar";
        staff.append(element);
        return { element, rhythm, withTime: index === 0 };
      });
    });
    drawParts(bars, width);
  }

  // A turned phone or a resized window draws the staves again for their new width.
  let resizeTimer = 0;
  window.addEventListener("resize", () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => {
      if (list.clientWidth !== drawnFor) drawStaves();
    }, 150);
  });

  function select(button) {
    optionButtons().forEach((option) => {
      const on = option === button;
      option.classList.toggle("selected", on);
      option.setAttribute("aria-pressed", String(on));
    });
    selected = button.value;
  }

  function showOptions(rhythms) {
    list.replaceChildren();
    selected = "";
    rhythms.forEach((rhythm, index) => {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "aa-rhythm-option";
      button.value = rhythm;
      button.setAttribute("aria-pressed", "false");
      button.setAttribute(
        "aria-label",
        format(loc.rhythmOptionLabel, [index + 1, AAStaffEntryExercise.spokenRhythm(notesOf(rhythm))])
      );
      const number = document.createElement("span");
      number.className = "aa-rhythm-number";
      number.setAttribute("aria-hidden", "true");
      number.textContent = String(index + 1);
      const staff = document.createElement("span");
      staff.className = "aa-rhythm-staff";
      staff.setAttribute("aria-hidden", "true");
      staff.dataset.rhythm = rhythm;
      button.append(number, staff);
      button.addEventListener("click", () => select(button));
      list.append(button);
    });
    if (placeholder) placeholder.hidden = rhythms.length > 0;
    drawStaves();
  }

  // Once answered, the round's rhythms stay on screen with the one played marked, until the next Play.
  function settle(answer) {
    optionButtons().forEach((option) => {
      const picked = option.classList.contains("selected");
      option.classList.remove("selected");
      option.setAttribute("aria-pressed", "false");
      option.classList.toggle("is-answer", option.value === answer);
      option.classList.toggle("is-wrong", picked && option.value !== answer);
      option.disabled = true;
    });
    selected = "";
  }

  // A wrong answer and "Show answer" draw the rhythm played on a staff.
  AAPractice.setAnswerView((answer) => {
    const notes = notesOf(answer);
    return {
      staff: { rhythm: true, timeSignature, notes },
      label: AAStaffEntryExercise.spokenRhythm(notes),
    };
  });

  // Turning free practice on or off drops the round on screen.
  AAPractice.onReset(() => {
    playToken = null;
    roundId = null;
    showOptions([]);
  });

  document.getElementById("Play")?.addEventListener("click", () => {
    AAPractice.play({
      exerciseId,
      filters: { grpLevel: levelSelect?.value || "1", grpTempo: tempoSelect?.value || "120" },
    }).then((data) => {
      if (AAi18n.serverError(data, loc)) return;
      playToken = data.playToken;
      roundId = data.roundId;
      const metadata = data.metadata || {};
      timeSignature = metadata.timeSignature || "4/4";
      showOptions(Array.isArray(metadata.options) ? metadata.options : []);
      if (playToken) AudioEngine.playToken(playToken);
    });
  });

  document.getElementById("Replay")?.addEventListener("click", () => {
    if (!playToken) {
      AAi18n.noAudio(loc);
      return;
    }
    AudioEngine.playToken(playToken);
  });

  document.getElementById("validateGuess")?.addEventListener("click", () => {
    if (!selected || !roundId) {
      AAi18n.incomplete(loc);
      return;
    }
    AAPractice.validate({
      exerciseId,
      roundId,
      userGuess: selected,
    }).then((data) => {
      if (AAi18n.serverError(data, loc)) return;
      AAi18n.result(data, loc);
      settle(data.answer);
      playToken = null;
      roundId = null;
    });
  });
});
