document.addEventListener("DOMContentLoaded", () => {
  //Iniciate Audio
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  //Iniciate Variables
  const loc = document.getElementById("localizer").dataset;

  let note1 = null;
  let note2 = null;
  let selectedGuess = "";
  let exerciseStartTime = Date.now();

  const exerciseIdInput = document.getElementById("exerciseId");
  const exerciseId = exerciseIdInput ? exerciseIdInput.value : null;

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
      const tonic = document.getElementById("keySelect")?.value || "C";
      const scaleType = document.getElementById("scaleTypeSelect")?.value || "major";

      fetch("/Exercise/RequestPlay", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          exerciseId: exerciseId,
          filters: {
            keySelect: tonic,
            scaleTypeSelect: scaleType,
          },
        }),
      })
        .then((resp) => resp.json())
        .then((data) => {
          note1 = data.note1;
          note2 = data.note2;

          if (note1 && note2) {
            AudioEngine.playSequence([note1, note2], 0.6);
          }
        })
        .catch((err) => {
          console.error("Erro ao preparar intervalo:", err);
        });
    });
  }

  const replayBtn = document.getElementById("Replay");
  if (replayBtn) {
    replayBtn.addEventListener("click", () => {
      if (!note1 || !note2) {
        Swal.fire({
          icon: "warning",
          title: loc.incompleteTitle,
          text: loc.incompleteText,
        });
        return;
      }
      AudioEngine.playSequence([note1, note2], 0.4);
    });
  }

  const n1Btn = document.getElementById("Note1");
  if (n1Btn) {
    n1Btn.addEventListener("click", () => {
      if (note1) {
        AudioEngine.playNote(note1, 1);
      }
    });
  }

  const n2Btn = document.getElementById("Note2");
  if (n2Btn) {
    n2Btn.addEventListener("click", () => {
      if (note2) {
        AudioEngine.playNote(note2, 1);
      }
    });
  }

  //Inciate Validate Event
  const validateBtn = document.getElementById("validateGuess");
  if (validateBtn) {
    validateBtn.addEventListener("click", () => {
      const correctCountEl = document.getElementById("correctCount");
      const errorCountEl = document.getElementById("errorCount");

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
				Swal.fire("Correct!", "You got the interval right!", "success");
			} else {
				if (errorCountEl) {
					errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
				}
				Swal.fire("Wrong!", `The correct interval was ${data.answer}.`, "error");
			}

          // Reset
          selectedGuess = "";
          note1 = null;
          note2 = null;
          guessButtons.forEach((btn) => btn.classList.remove("selected"));
        });
    });
  }
});
