document.addEventListener("DOMContentLoaded", () => {
  //Iniciate Audio
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = document.getElementById("localizer").dataset;
  //Iniciate Variables
  let melody1 = [];
  let melody2 = [];
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

  const playBtn = document.getElementById("Play");
  if (playBtn) {
    playBtn.addEventListener("click", () => {
      const length = document.getElementById("melodyLength")?.value || 5;

      fetch("/Exercise/RequestPlay", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          exerciseId: exerciseId,
          filters: {
            melodyLength: length,
          },
        }),
      })
        .then((resp) => resp.json())
        .then((data) => {
          melody1 = data.melody1;
          melody2 = data.melody2;
          AcademiaAuditiva.audio.playMelodyWithRhythm(melody1);
          const totalTime = melody1.reduce((sum, n) => sum + n.duration, 0);

          setTimeout(() => {
            AcademiaAuditiva.audio.playMelodyWithRhythm(melody2);
          }, (totalTime + 1) * 1000);
        })
        .catch((err) => {
          console.error("Erro ao gerar melodias:", err);
        });
    });
  }

  const replayBtn = document.getElementById("Replay");
  if (replayBtn) {
    replayBtn.addEventListener("click", () => {
      if (!melody1.length || !melody2.length) {
        Swal.fire({
          icon: "warning",
          title: loc.incompleteTitle,
          text: loc.incompleteText,
        });
        return;
      }
      AudioEngine.playSequence(melody1, 0.35, () => {
        setTimeout(() => AudioEngine.playSequence(melody2, 0.35), 600);
      });
    });
  }

  const m1Btn = document.getElementById("Melody1");
  if (m1Btn) {
    m1Btn.addEventListener("click", () => {
      if (melody1.length) AudioEngine.playSequence(melody1, 0.35);
    });
  }

  const m2Btn = document.getElementById("Melody2");
  if (m2Btn) {
    m2Btn.addEventListener("click", () => {
      if (melody2.length) AudioEngine.playSequence(melody2, 0.35);
    });
  }

  const validateBtn = document.getElementById("validateGuess");
  if (validateBtn) {
    validateBtn.addEventListener("click", () => {
      if (!selectedGuess || !melody1.length || !melody2.length) {
        Swal.fire({
          icon: "warning",
          title: loc.incompleteTitle,
          text: loc.incompleteText,
        });
        return;
      }

      const isEqual = JSON.stringify(melody1) === JSON.stringify(melody2);
      const userIsCorrect =
        (isEqual && selectedGuess === "same") ||
        (!isEqual && selectedGuess === "diff");

      fetch("/Exercise/ValidateExercise", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          exerciseId: exerciseId,
          userGuess: selectedGuess,
          actualAnswer: isEqual ? "same" : "diff",
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
              `The correct answer was ${data.answer}.`,
              "error"
            );
          }

          selectedGuess = "";
          melody1 = [];
          melody2 = [];
          guessButtons.forEach((btn) => btn.classList.remove("selected"));
        });
    });
  }
});
