document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = document.getElementById("localizer").dataset;
  const exerciseId = document.getElementById("exerciseId")?.value;

  let playToken = null;
  let roundId = null;
  let staffInstance = null;
  let promptRoot = null;
  const exerciseStartTime = Date.now();

  function mountEditor(rootNote) {
    const target = document.getElementById("staffEditor");
    target.innerHTML = "";
    const octave = parseInt(String(rootNote).match(/\d+/)?.[0] || "4", 10);
    staffInstance = window.StaffEditor.attach("#staffEditor", {
      clef: "treble",
      keySignature: "C",
      timeSignature: null,
      octave: octave,
      allowedDurations: ["w"],
      restDurations: [],
      showBarline: false,
      totalSlots: 10,
      prefilledNotes: [{ note: rootNote, duration: "w" }],
      onChange: function () { /* no-op */ },
    });
  }

  // Render an empty staff initially so the user sees the canvas.
  window.StaffRenderer.render("#staffEditor", {
    clef: "treble",
    keySignature: "C",
    notes: [],
  });

  const playBtn = document.getElementById("Play");
  if (playBtn) {
    playBtn.addEventListener("click", () => {
      if (!exerciseId) return;
      fetch("/Exercise/RequestPlay", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ exerciseId: exerciseId }),
      })
        .then((r) => r.json())
        .then((data) => {
          playToken = data.playToken;
          roundId = data.roundId;
          // Server returns the prompt note inside metadata for staff exercises.
          const meta = data.metadata || {};
          promptRoot = (meta.promptNotes && meta.promptNotes[0]) || null;
          if (promptRoot) mountEditor(promptRoot);
          if (playToken) AudioEngine.playToken(playToken);
        })
        .catch((err) => console.error("Erro ao preparar CompleteScale:", err));
    });
  }

  const replayBtn = document.getElementById("Replay");
  if (replayBtn) {
    replayBtn.addEventListener("click", () => {
      if (!playToken) {
        Swal.fire({ icon: "warning", title: loc.incompleteTitle, text: loc.incompleteText });
        return;
      }
      AudioEngine.playToken(playToken);
    });
  }

  const validateBtn = document.getElementById("validateGuess");
  if (validateBtn) {
    validateBtn.addEventListener("click", () => {
      if (!staffInstance || !roundId) {
        Swal.fire({ icon: "warning", title: loc.incompleteTitle, text: loc.incompleteText });
        return;
      }
      const userAnswer = staffInstance.getAnswerString();
      if (!userAnswer) {
        Swal.fire({ icon: "warning", title: loc.incompleteTitle, text: loc.incompleteText });
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
          const cc = document.getElementById("correctCount");
          const ec = document.getElementById("errorCount");

          if (data.isCorrect) {
            if (cc) cc.innerText = parseInt(cc.innerText) + 1;
            Swal.fire(loc.correctMessage, loc.correctMessageText, "success");
          } else {
            if (ec) ec.innerText = parseInt(ec.innerText) + 1;
            Swal.fire(loc.wrongMessage, `${loc.wrongMessageText} ${data.answer || ""}`, "error");
          }

          if (staffInstance) staffInstance.clear();
          playToken = null;
          roundId = null;
          promptRoot = null;
        });
    });
  }
});
