document.addEventListener("DOMContentLoaded", () => {
  //Iniciate Audio
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = document.getElementById("localizer").dataset;
  //Iniciate Variables
  let selectedGuess = "";
  let chordNotes = [];
  let exerciseStartTime = Date.now();

  const exerciseId = document.getElementById("exerciseId")?.value;
  const keySelect = document.getElementById("keySelect");
  const scaleTypeSelect = document.getElementById("scaleTypeSelect");

  //Iniciate Click Events
  const guessButtons = document.querySelectorAll(".guessAnswer");
  guessButtons.forEach((button) => {
    button.addEventListener("click", (e) => {
      guessButtons.forEach((btn) => btn.classList.remove("selected"));
      e.target.classList.add("selected");
      selectedGuess = e.target.value;
    });
  });

  //Inciate Play and Replay Events
  const playBtn = document.getElementById("Play");
  if (playBtn) {
    playBtn.addEventListener("click", () => {
      const key = keySelect?.value || "C";
      const scaleType = scaleTypeSelect?.value || "major";

      toggleFunctionButtons(scaleType);

      fetch("/Exercise/RequestPlay", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          exerciseId: exerciseId,
          filters: {
            keySelect: key,
            scaleTypeSelect: scaleType,
          },
        }),
      })
        .then((resp) => resp.json())
        .then((data) => {
          chordNotes = data.notes;
          AudioEngine.playChord(chordNotes, 1);
        });
    });
  }

  const replayBtn = document.getElementById("Replay");
  if (replayBtn) {
    replayBtn.addEventListener("click", () => {
      if (!chordNotes || chordNotes.length === 0) {
        Swal.fire({
          icon: "warning",
          title: loc.incompleteTitle,
          text: loc.incompleteText,
        });
        return;
      }
      AudioEngine.playChord(chordNotes, 1);
    });
  }

  //Inciate Validate Event
  const validateBtn = document.getElementById("validateGuess");
  if (validateBtn) {
    validateBtn.addEventListener("click", () => {
      if (!selectedGuess) {
        Swal.fire({
          icon: "warning",
          title: loc.incompleteTitle,
          text: loc.incompleteText,
        });
        return;
      }

      fetch("/Exercise/ValidateExercise", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          exerciseId: exerciseId,
          userGuess: selectedGuess,
          timeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000),
        }),
      })
        .then((resp) => resp.json())
        .then((data) => {
          const correctCountEl = document.getElementById("correctCount");
          const errorCountEl = document.getElementById("errorCount");
          if (data.isCorrect) {
            if (correctCountEl) {
              correctCountEl.innerText = parseInt(correctCountEl.innerText) + 1;
            }
            Swal.fire("Correct!", "You got it right!", "success");
          } else {
            if (errorCountEl) {
              errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
            }
            Swal.fire(
              "Wrong!",
              `The correct answer was ${data.answer.replace("|", " ")}.`,
              "error"
            );
          }

          // Reset
          selectedGuess = "";
          chordNotes = [];
          guessButtons.forEach((btn) => btn.classList.remove("selected"));
        });
    });
  }

  //Inciate Filter Events
  function toggleFunctionButtons(scaleType) {
    const majorFunctions = ["I", "ii", "iii", "IV", "V", "vi", "VII°"];
    const minorFunctions = ["i", "II°", "III", "iv", "v", "VI", "VII"];

    const guessButtons = document.querySelectorAll(".guessAnswer");

    guessButtons.forEach((btn) => {
      const label = btn.innerText.trim();
      if (scaleType === "major") {
        btn.style.display = majorFunctions.includes(label)
          ? "inline-block"
          : "none";
      } else if (scaleType === "minor") {
        btn.style.display = minorFunctions.includes(label)
          ? "inline-block"
          : "none";
      }
    });
  }

  scaleTypeSelect?.addEventListener("change", () => {
    toggleFunctionButtons(scaleTypeSelect.value);
  });
  toggleFunctionButtons(scaleTypeSelect?.value || "major");
});
