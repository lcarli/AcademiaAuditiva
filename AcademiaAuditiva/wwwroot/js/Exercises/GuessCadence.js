document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = AAi18n.localizer();
  const exerciseId = document.getElementById("exerciseId")?.value;
  let playToken = null;
  let roundId = null;
  let selectedGuess = "";

  const guessButtons = document.querySelectorAll(".guessAnswer");
  guessButtons.forEach((button) => {
    button.addEventListener("click", () => {
      selectedGuess = button.value;
      guessButtons.forEach((btn) => btn.classList.remove("selected"));
      button.classList.add("selected");
    });
  });

  // Turning free practice on or off drops the round on screen.
  AAPractice.onReset(() => {
    playToken = null;
    roundId = null;
  });

  document.getElementById("Play")?.addEventListener("click", () => {
    if (!exerciseId) return;
    AAPractice.play({ exerciseId }).then((data) => {
      if (AAi18n.serverError(data, loc)) return;
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
    AAPractice.validate({
      ExerciseId: exerciseId,
      RoundId: roundId,
      userGuess: selectedGuess,
    }).then((data) => {
      if (AAi18n.serverError(data, loc)) return;
      AAi18n.result(data, loc);
      selectedGuess = "";
      playToken = null;
      roundId = null;
      guessButtons.forEach((btn) => btn.classList.remove("selected"));
    });
  });
});
