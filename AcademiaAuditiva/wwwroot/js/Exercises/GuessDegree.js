document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  AudioEngine.setupWaveform();

  const loc = AAi18n.localizer();
  let playToken = null;
  let roundId = null;
  let selectedGuess = "";

  const exerciseId = document.getElementById("exerciseId")?.value;
  const keySelect = document.getElementById("keySelect");
  const scaleTypeSelect = document.getElementById("scaleTypeSelect");
  const levelSelect = document.getElementById("gdLevel");

  // The degrees asked in each kind of key on each level, as the server draws them
  // (MusicTheoryService): a minor key counts them on its natural minor scale.
  const degrees = {
    major: {
      diatonic: ["1", "2", "3", "4", "5", "6", "7"],
      chromatic: ["1", "b2", "2", "b3", "3", "4", "#4", "5", "b6", "6", "b7", "7"],
    },
    minor: {
      diatonic: ["1", "2", "3", "4", "5", "6", "7"],
      chromatic: ["1", "b2", "2", "3", "#3", "4", "#4", "5", "6", "#6", "7", "#7"],
    },
  };

  const scaleType = () => (scaleTypeSelect?.value === "minor" ? "minor" : "major");
  const level = () => (levelSelect?.value === "chromatic" ? "chromatic" : "diatonic");

  const guessButtons = document.querySelectorAll(".guessAnswer");
  guessButtons.forEach((button) => {
    button.addEventListener("click", () => {
      guessButtons.forEach((btn) => btn.classList.remove("selected"));
      button.classList.add("selected");
      selectedGuess = button.value;
    });
  });

  // Shows the degrees of the scale and level picked; a hidden answer can't stay selected.
  function toggleDegreeButtons() {
    const shown = degrees[scaleType()][level()];
    guessButtons.forEach((btn) => {
      const visible = shown.includes(btn.value);
      btn.style.display = visible ? "" : "none";
      if (!visible && btn.classList.contains("selected")) {
        btn.classList.remove("selected");
        selectedGuess = "";
      }
    });
  }

  // Turning free practice on or off drops the round on screen.
  AAPractice.onReset(() => {
    playToken = null;
    roundId = null;
  });

  const playBtn = document.getElementById("Play");
  if (playBtn) {
    playBtn.addEventListener("click", () => {
      toggleDegreeButtons();

      AAPractice.play({
        exerciseId: exerciseId,
        filters: { keySelect: keySelect?.value || "C", scaleTypeSelect: scaleType(), gdLevel: level() },
      }).then((data) => {
        if (AAi18n.serverError(data, loc)) return;
        playToken = data.playToken;
        roundId = data.roundId;
        if (playToken) AudioEngine.playToken(playToken);
      });
    });
  }

  const replayBtn = document.getElementById("Replay");
  if (replayBtn) {
    replayBtn.addEventListener("click", () => {
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
      if (!selectedGuess || !roundId) {
        AAi18n.incomplete(loc);
        return;
      }

      AAPractice.validate({
        exerciseId: exerciseId,
        roundId: roundId,
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
  }

  scaleTypeSelect?.addEventListener("change", toggleDegreeButtons);
  levelSelect?.addEventListener("change", toggleDegreeButtons);
  toggleDegreeButtons();
});
