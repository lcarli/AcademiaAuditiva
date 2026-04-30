document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const exerciseId = document.getElementById("exerciseId")?.value;

  // The front-end never learns the actual note. It only holds the
  // token (to replay) and the roundId (to validate against the same
  // round). Both come from RequestPlay; both are opaque GUIDs.
  let playToken = null;
  let roundId = null;
  let userGuessedNote = "";
  const exerciseStartTime = Date.now();

  const guessButtons = document.querySelectorAll(".guessAnswer");
  guessButtons.forEach((button) => {
    button.addEventListener("click", () => {
      userGuessedNote = button.value;
      guessButtons.forEach((btn) => btn.classList.remove("selected"));
      button.classList.add("selected");
    });
  });

  const playButton = document.getElementById("Play");
  if (playButton) {
    playButton.addEventListener("click", () => {
      if (!exerciseId) return;
      fetch("/Exercise/RequestPlay", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ exerciseId: exerciseId }),
      })
        .then((resp) => resp.json())
        .then((data) => {
          playToken = data.playToken;
          roundId = data.roundId;
          if (playToken) AudioEngine.playToken(playToken);
        });
    });
  }

  const replayButton = document.getElementById("Replay");
  if (replayButton) {
    replayButton.addEventListener("click", () => {
      if (!playToken) {
        Swal.fire({
          icon: "warning",
          title: "No note loaded",
          text: "Click 'Play' first to generate the note.",
        });
        return;
      }
      AudioEngine.playToken(playToken);
    });
  }

  const validateBtn = document.getElementById("validateGuess");
  if (validateBtn) {
    validateBtn.addEventListener("click", () => {
      if (!userGuessedNote || !roundId) {
        Swal.fire({
          icon: "warning",
          title: "Missing data",
          text: "Generate a note and select your answer before validating.",
        });
        return;
      }
      fetch("/Exercise/ValidateExercise", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          ExerciseId: exerciseId,
          RoundId: roundId,
          userGuess: userGuessedNote,
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
            Swal.fire("Correct!", "You got the note right!", "success");
          } else {
            if (errorCountEl) {
              errorCountEl.innerText = parseInt(errorCountEl.innerText) + 1;
            }
            Swal.fire(
              "Wrong!",
              `The correct note was ${(data.answer || "")
                .replace(/\d/g, "")
                .toUpperCase()}.`,
              "error"
            );
          }
          userGuessedNote = "";
          playToken = null;
          roundId = null;
          guessButtons.forEach((btn) => btn.classList.remove("selected"));
        });
    });
  }
});
