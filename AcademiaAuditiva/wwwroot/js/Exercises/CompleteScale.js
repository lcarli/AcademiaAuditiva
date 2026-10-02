document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = AAi18n.localizer();
  const exerciseId = document.getElementById("exerciseId")?.value;
  const prompt = document.getElementById("staffPrompt");
  const initialPrompt = prompt ? prompt.textContent : "";
  let playToken = null;
  let roundId = null;
  let metadata = null;
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
      selectedNoteLabel: loc.selectedNoteLabel,
      noSelectionLabel: loc.noSelectionLabel,
      prefilledNotes: [{ note: rootNote, duration: "w" }],
    });
  }

  function drawEmptyStaff() {
    window.StaffRenderer.render("#staffEditor", {
      clef: "treble",
      keySignature: "C",
      notes: [],
    });
  }

  drawEmptyStaff();

  // The answer leaves out the root, which the editor shows in gray.
  AAPractice.setAnswerView((answer) => {
    const root = metadata?.promptNotes?.[0];
    return {
      staff: {
        clef: "treble",
        keySignature: "C",
        notes: (root ? [{ note: root, duration: "w", prefilled: true }] : []).concat(AAPractice.staffNotes(answer)),
      },
    };
  });

  // Turning free practice on or off drops the round on screen.
  AAPractice.onReset(() => {
    playToken = null;
    roundId = null;
    metadata = null;
    if (staffInstance) staffInstance.destroy();
    staffInstance = null;
    if (prompt) prompt.textContent = initialPrompt;
    drawEmptyStaff();
  });

  document.getElementById("Play")?.addEventListener("click", () => {
    if (!exerciseId) return;
    AAPractice.play({ exerciseId, filters: filterValues() }).then((data) => {
      if (AAi18n.serverError(data, loc)) return;
      playToken = data.playToken;
      roundId = data.roundId;
      metadata = data.metadata || {};
      const promptRoot = metadata.promptNotes?.[0];
      setPrompt(metadata);
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

    AAPractice.validate({
      ExerciseId: exerciseId,
      RoundId: roundId,
      userGuess: userAnswer,
      timeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000),
    }).then((data) => {
      if (AAi18n.serverError(data, loc)) return;
      AAi18n.result(data, loc);
      staffInstance?.clear();
      playToken = null;
      roundId = null;
    });
  });
});
