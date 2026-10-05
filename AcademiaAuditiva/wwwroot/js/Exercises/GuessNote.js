document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = AAi18n.localizer();
  const exerciseId = document.getElementById("exerciseId")?.value;

  // The front-end never learns the actual note. It only holds the
  // token (to replay) and the roundId (to validate against the same
  // round). Both come from RequestPlay; both are opaque GUIDs.
  let playToken = null;
  let roundId = null;
  let userGuessedNote = "";

  const guessButtons = document.querySelectorAll(".guessAnswer");
  guessButtons.forEach((button) => {
    button.addEventListener("click", () => {
      userGuessedNote = button.value;
      guessButtons.forEach((btn) => btn.classList.remove("selected"));
      button.classList.add("selected");
    });
  });

  // Turning free practice on or off drops the round on screen.
  AAPractice.onReset(() => {
    playToken = null;
    roundId = null;
  });

  const playButton = document.getElementById("Play");
  if (playButton) {
    playButton.addEventListener("click", () => {
      if (!exerciseId) return;
      AAPractice.play({ exerciseId: exerciseId }).then((data) => {
        if (AAi18n.serverError(data, loc)) return;
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
        AAi18n.noAudio(loc);
        return;
      }
      AudioEngine.playToken(playToken);
    });
  }

  const validateBtn = document.getElementById("validateGuess");
  if (validateBtn) {
    validateBtn.addEventListener("click", () => {
      if (!userGuessedNote || !roundId) {
        AAi18n.incomplete(loc);
        return;
      }
      AAPractice.validate({
        ExerciseId: exerciseId,
        RoundId: roundId,
        userGuess: userGuessedNote,
      }).then((data) => {
        if (AAi18n.serverError(data, loc)) return;
        AAi18n.result(data, loc);
        userGuessedNote = "";
        playToken = null;
        roundId = null;
        guessButtons.forEach((btn) => btn.classList.remove("selected"));
      });
    });
  }
});
