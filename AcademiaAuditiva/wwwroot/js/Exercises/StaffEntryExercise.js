(function (window, document) {
  "use strict";

  function format(template, values) {
    return String(template || "").replace(/\{(\w+)\}/g, (_, key) => values[key] ?? "");
  }

  function promptFor(exerciseName, loc, metadata) {
    if (exerciseName === "CompleteChord") {
      return format(loc.completeChordPrompt, { root: metadata.promptNotes?.[0] || "" });
    }
    if (exerciseName === "TransposeScale") {
      return format(loc.transposeScalePrompt, {
        from: metadata.originalRoot || "",
        to: metadata.targetRoot || "",
        scale: metadata.scale || "",
      });
    }
    if (exerciseName === "MelodicDictation") {
      return format(loc.melodicDictationPrompt, { time: metadata.timeSignature || "4/4" });
    }
    if (exerciseName === "RhythmDictation") {
      return format(loc.rhythmDictationPrompt, { time: metadata.timeSignature || "4/4" });
    }
    return "";
  }

  function optionsFor(exerciseName, metadata) {
    const octave = Number(metadata.octave || 4);
    if (exerciseName === "CompleteChord") {
      return {
        clef: "treble",
        keySignature: "C",
        octave,
        allowedDurations: ["w"],
        restDurations: [],
        showBarline: false,
        totalSlots: 4,
        prefilledNotes: (metadata.promptNotes || []).map((note) => ({ note, duration: "w" })),
      };
    }
    if (exerciseName === "TransposeScale") {
      return {
        clef: "treble",
        keySignature: "C",
        octave,
        allowedDurations: ["q"],
        restDurations: [],
        showBarline: false,
        totalSlots: 8,
      };
    }
    return {
      clef: "treble",
      keySignature: "C",
      timeSignature: metadata.timeSignature || "4/4",
      octave,
      allowedDurations: ["w", "h", "q", "8"],
      restDurations: ["wr", "hr", "qr", "8r"],
      showBarline: true,
      totalSlots: 64,
    };
  }

  window.AAStaffEntryExercise = {
    init() {
      AcademiaAuditiva.init();
      AudioEngine.setupWaveform();

      const loc = AAi18n.localizer();
      const exerciseId = document.getElementById("exerciseId")?.value;
      const exerciseName = document.getElementById("staffExerciseName")?.value;
      let playToken = null;
      let roundId = null;
      let staffInstance = null;
      const exerciseStartTime = Date.now();

      window.StaffRenderer.render("#staffEditor", {
        clef: "treble",
        keySignature: "C",
        notes: [],
      });

      document.getElementById("Play")?.addEventListener("click", () => {
        if (!exerciseId || !exerciseName) return;
        fetch("/Exercise/RequestPlay", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ exerciseId }),
        })
          .then((r) => r.json())
          .then((data) => {
            playToken = data.playToken;
            roundId = data.roundId;
            const metadata = data.metadata || {};
            const prompt = document.getElementById("staffPrompt");
            if (prompt) prompt.textContent = promptFor(exerciseName, loc, metadata);
            staffInstance = window.StaffEditor.attach("#staffEditor", optionsFor(exerciseName, metadata));
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
    },
  };
})(window, document);
