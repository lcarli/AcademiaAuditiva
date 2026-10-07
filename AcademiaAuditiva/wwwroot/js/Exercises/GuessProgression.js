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
  const levelSelect = document.getElementById("gpLevel");
  const namesPanel = document.getElementById("progressionNames");
  const numeralsPanel = document.getElementById("progressionNumerals");
  const chordSelects = ["chord2Select", "chord3Select", "chord4Select"]
    .map((id) => document.getElementById(id))
    .filter(Boolean);

  // The progressions named in each kind of key, as the server draws them (MusicTheoryService).
  const progressions = {
    major: ["I-V-vi-IV", "I-vi-IV-V", "ii-V-I", "blues"],
    minor: ["i-VII-VI-V", "i-VI-III-VII", "iio-V-i"],
  };

  // The chords of each kind of key that dictation asks, from the tonic. A minor key's VII is
  // the natural one and its V has the leading tone: the same chord as in a major key.
  const chords = {
    major: [["1-major", "I"], ["2-minor", "ii"], ["3-minor", "iii"], ["4-major", "IV"], ["5-major", "V"], ["6-minor", "vi"], ["7-diminished", "vii°"]],
    minor: [["1-minor", "i"], ["2-diminished", "ii°"], ["3-major", "III"], ["4-minor", "iv"], ["5-major", "V"], ["6-major", "VI"], ["7-major", "VII"]],
  };
  const numerals = new Map([...chords.major, ...chords.minor]);
  const majorChords = new Set(chords.major.map(([code]) => code));

  const scaleType = () => (scaleTypeSelect?.value === "minor" ? "minor" : "major");
  const level = () => (levelSelect?.value === "numerals" ? "numerals" : "name");

  const guessButtons = document.querySelectorAll(".guessAnswer");
  guessButtons.forEach((button) => {
    button.addEventListener("click", () => {
      guessButtons.forEach((btn) => btn.classList.remove("selected"));
      button.classList.add("selected");
      selectedGuess = button.value;
    });
  });

  function clearAnswer() {
    selectedGuess = "";
    guessButtons.forEach((btn) => btn.classList.remove("selected"));
    chordSelects.forEach((select) => {
      select.value = "";
    });
  }

  // Shows the answers of the level and kind of key picked: the key's progressions, or a
  // select of its chords for each chord after the tonic.
  function showAnswers() {
    const naming = level() === "name";
    if (namesPanel) namesPanel.hidden = !naming;
    if (numeralsPanel) numeralsPanel.hidden = naming;

    const shown = progressions[scaleType()];
    guessButtons.forEach((btn) => {
      btn.style.display = shown.includes(btn.value) ? "" : "none";
    });

    chordSelects.forEach((select) => {
      const placeholder = select.options[0];
      select.replaceChildren(placeholder, ...chords[scaleType()].map(([code, numeral]) => new Option(numeral, code)));
    });
  }

  // A dictation answer ("6-minor|2-minor|5-major") as the four chords from the tonic,
  // "I – vi – ii – V": a chord out of the major key means a minor one. A name reads as its button.
  function formatAnswer(answer) {
    const parts = String(answer || "").split("|");
    if (parts.length !== 3 || !parts.every((code) => numerals.has(code))) return AAi18n.answerLabel(answer);
    const tonic = parts.some((code) => !majorChords.has(code)) ? "i" : "I";
    return [tonic, ...parts.map((code) => numerals.get(code))].join(" – ");
  }

  AAPractice.setAnswerView(formatAnswer);

  function showRequestError(err) {
    console.error("GuessProgression request failed:", err);
    Swal.fire({ icon: "error", title: loc.validationErrorTitle, text: loc.validationErrorText });
  }

  // Turning free practice on or off drops the round on screen.
  AAPractice.onReset(() => {
    playToken = null;
    roundId = null;
  });

  const playBtn = document.getElementById("Play");
  if (playBtn) {
    playBtn.addEventListener("click", () => {
      AAPractice.play({
        exerciseId: exerciseId,
        filters: { keySelect: keySelect?.value || "C", scaleTypeSelect: scaleType(), gpLevel: level() },
      })
        .then((data) => {
          if (AAi18n.serverError(data, loc)) return;
          playToken = data.playToken;
          roundId = data.roundId;
          clearAnswer();
          if (playToken) AudioEngine.playToken(playToken);
        })
        .catch(showRequestError);
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
      if (!roundId) {
        AAi18n.noAudio(loc);
        return;
      }

      const guess = level() === "numerals" ? chordSelects.map((select) => select.value) : [selectedGuess];
      if (guess.some((part) => !part)) {
        AAi18n.incomplete(loc);
        return;
      }

      AAPractice.validate({
        exerciseId: exerciseId,
        roundId: roundId,
        userGuess: guess.join("|"),
      })
        .then((data) => {
          if (AAi18n.serverError(data, loc)) return;
          AAi18n.result(data.isCorrect ? data : { ...data, answer: formatAnswer(data.answer) }, loc);

          clearAnswer();
          playToken = null;
          roundId = null;
        })
        .catch(showRequestError);
    });
  }

  // The round on screen is in the kind of key and level it was played in: changing either drops it.
  function changeAnswers() {
    playToken = null;
    roundId = null;
    clearAnswer();
    showAnswers();
  }

  scaleTypeSelect?.addEventListener("change", changeAnswers);
  levelSelect?.addEventListener("change", changeAnswers);
  showAnswers();
});
