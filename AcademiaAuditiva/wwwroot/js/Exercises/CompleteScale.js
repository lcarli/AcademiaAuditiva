document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = AAi18n.localizer();
  const exerciseId = document.getElementById("exerciseId")?.value;
  let playToken = null;
  let roundId = null;
  let staffInstance = null;
  const exerciseStartTime = Date.now();

  function filterValues() {
    const filters = {};
    document.querySelectorAll("#filtersModal select").forEach((select) => {
      filters[select.name || select.id] = select.value;
    });
    return filters;
  }

  function scaleLabel(scale) {
    const key = "scale" + String(scale || "").charAt(0).toUpperCase() + String(scale || "").slice(1);
    return loc[key] || scale || "";
  }

  function setPrompt(metadata) {
    const prompt = document.getElementById("staffPrompt");
    if (!prompt) return;
    prompt.textContent = String(loc.completeScalePrompt || "").replace("{{0}}", scaleLabel(metadata.scale));
  }

  function mountEditor(rootNote) {
    const octave = parseInt(String(rootNote).match(/\d+/)?.[0] || "4", 10);
    staffInstance = window.StaffEditor.attach("#staffEditor", {
      clef: "treble",
      keySignature: "C",
      timeSignature: null,
      octave,
      allowedDurations: ["w"],
      restDurations: [],
      showBarline: false,
      totalSlots: 10,
      minOctave: 3,
      maxOctave: 6,
      octaveDisplayLabel: loc.octaveLabel,
      octaveDownLabel: loc.octaveDownLabel,
      octaveUpLabel: loc.octaveUpLabel,
      prefilledNotes: [{ note: rootNote, duration: "w" }],
    });
  }

  window.StaffRenderer.render("#staffEditor", {
    clef: "treble",
    keySignature: "C",
    notes: [],
  });

  document.getElementById("Play")?.addEventListener("click", () => {
    if (!exerciseId) return;
    fetch("/Exercise/RequestPlay", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ exerciseId, filters: filterValues() }),
    })
      .then((r) => r.json())
      .then((data) => {
        playToken = data.playToken;
        roundId = data.roundId;
        const promptRoot = data.metadata?.promptNotes?.[0];
        setPrompt(data.metadata || {});
        if (promptRoot) mountEditor(promptRoot);
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
    if (!staffInstance || !roundId) {
      AAi18n.incomplete(loc);
      return;
    }
    const userAnswer = staffInstance.getAnswerString();
    if (!userAnswer) {
      AAi18n.incomplete(loc);
      return;
    }

    fetch("/Exercise/ValidateExercise", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        ExerciseId: exerciseId,
        RoundId: roundId,
        userGuess: userAnswer,
        timeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000),
      }),
    })
      .then((r) => r.json())
      .then((data) => {
        if (AAi18n.serverError(data, loc)) return;
        const counter = document.getElementById(data.isCorrect ? "correctCount" : "errorCount");
        if (counter) counter.innerText = parseInt(counter.innerText) + 1;
        AAi18n.result(data, loc);
        staffInstance.clear();
        playToken = null;
        roundId = null;
      });
  });
});
