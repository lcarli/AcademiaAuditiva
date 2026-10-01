document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = AAi18n.localizer();
  const exerciseId = document.getElementById("exerciseId")?.value;
  let playToken = null;
  let roundId = null;
  let selectedGuess = "";
  const exerciseStartTime = Date.now();

  const guessButtons = document.querySelectorAll(".guessAnswer");
  guessButtons.forEach((button) => {
    button.addEventListener("click", () => {
      selectedGuess = button.value;
      guessButtons.forEach((btn) => btn.classList.remove("selected"));
      button.classList.add("selected");
    });
  });

  document.getElementById("Play")?.addEventListener("click", () => {
    if (!exerciseId) return;
    fetch("/Exercise/RequestPlay", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ exerciseId }),
    })
      .then((resp) => resp.json())
      .then((data) => {
        playToken = data.playToken;
        roundId = data.roundId;
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
    if (!selectedGuess || !roundId) {
      AAi18n.incomplete(loc);
      return;
    }
    fetch("/Exercise/ValidateExercise", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        ExerciseId: exerciseId,
        RoundId: roundId,
        userGuess: selectedGuess,
        timeSpentSeconds: Math.floor((Date.now() - exerciseStartTime) / 1000),
      }),
    })
      .then((resp) => resp.json())
      .then((data) => {
        if (AAi18n.serverError(data, loc)) return;
        const counter = document.getElementById(data.isCorrect ? "correctCount" : "errorCount");
        if (counter) counter.innerText = parseInt(counter.innerText) + 1;
        AAi18n.result(data, loc);
        selectedGuess = "";
        playToken = null;
        roundId = null;
        guessButtons.forEach((btn) => btn.classList.remove("selected"));
      });
  });
});
