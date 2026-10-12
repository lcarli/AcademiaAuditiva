document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  const loc = AAi18n.localizer();
  const exerciseId = document.getElementById("exerciseId")?.value;
  const levelSelect = document.getElementById(loc.levelFilter);
  const ab = ABPlayback.create(document.querySelector("[data-aa-ab]"));
  const buttons = [...document.querySelectorAll(".guessAnswer")];
  const directionButtons = [...document.querySelectorAll("[data-bc-direction]")];
  const validateButton = document.getElementById("validateGuess");
  let roundId = null;
  let answer = "";
  let direction = "";
  let validating = false;

  function select(selected) {
    buttons.forEach(button => {
      button.classList.toggle("selected", button === selected);
      button.setAttribute("aria-pressed", button === selected ? "true" : "false");
    });
    const region = selected?.value || "";
    answer = loc.exercise === "BoostOrCut"
      ? direction && region ? `${direction}|${region}` : ""
      : region;
  }

  function clearSelection() {
    direction = "";
    directionButtons.forEach(button => {
      button.classList.remove("selected");
      button.setAttribute("aria-pressed", "false");
    });
    select(null);
  }

  function applyAnswerLevels() {
    if (loc.exercise !== "GuessQ") return;
    const activeLevel = levelSelect?.value || "beginner";
    buttons.forEach(button => {
      const visible = button.dataset.levels.split(" ").includes(activeLevel);
      button.hidden = !visible;
      if (!visible && button.classList.contains("selected")) select(null);
    });
  }

  function clearRound() {
    roundId = null;
    clearSelection();
    ab?.clear();
  }

  function finishRound() {
    roundId = null;
    clearSelection();
  }

  buttons.forEach(button => button.addEventListener("click", () => select(button)));
  directionButtons.forEach(button => button.addEventListener("click", () => {
    direction = button.dataset.bcDirection;
    directionButtons.forEach(option => {
      const selected = option === button;
      option.classList.toggle("selected", selected);
      option.setAttribute("aria-pressed", selected ? "true" : "false");
    });
    const selectedRegion = buttons.find(option => option.classList.contains("selected"));
    answer = selectedRegion ? `${direction}|${selectedRegion.value}` : "";
  }));
  applyAnswerLevels();
  levelSelect?.addEventListener("change", () => {
    AAPractice.reset();
    applyAnswerLevels();
  });
  AAPractice.onReset(clearRound);

  document.getElementById("Play")?.addEventListener("click", () => {
    AAPractice.play({
      exerciseId, filters: { [loc.levelFilter]: levelSelect?.value || "beginner" },
    }).then(data => {
      if (AAi18n.serverError(data, loc)) return;
      try {
        ab.setRound(data.clips);
      } catch (error) {
        console.error("Invalid A/B round:", error);
        AAPractice.reset();
        Swal.fire({ icon: "error", title: loc.validationErrorTitle, text: loc.validationErrorText });
        return;
      }
      roundId = data.roundId;
      clearSelection();
      ab.play("A");
    });
  });

  validateButton?.addEventListener("click", async () => {
    if (validating) return;
    const activeRoundId = roundId;
    if (!activeRoundId || !answer) {
      AAi18n.incomplete(loc);
      return;
    }

    validating = true;
    validateButton.disabled = true;
    try {
      if (!await ab.whenReady()) {
        Swal.fire({ icon: "error", title: loc.validationErrorTitle, text: loc.validationErrorText });
        return;
      }

      const data = await AAPractice.validate({ exerciseId, roundId: activeRoundId, userGuess: answer });
      if (AAi18n.serverError(data, loc)) return;
      const detail = data.detail;
      const number = value => new Intl.NumberFormat(document.documentElement.lang).format(value);
      const values = loc.exercise === "BoostOrCut"
        ? [loc[detail.direction], loc[detail.region], number(detail.frequencyHz), number(detail.gainDb)]
        : loc.exercise === "GuessQ"
          ? [loc[detail.width] || detail.width, number(detail.frequencyHz), number(detail.q)]
          : loc.exercise === "Compression"
            ? [detail.compressedClip, number(detail.ratio), number(detail.attackMs), number(detail.releaseMs)]
            : [buttons.find(button => button.value === detail.answer).textContent.trim(), ...detail.parameters.map(number)];
      const text = loc.feedback.replace(/\{(\d+)\}/g, (_, index) => values[Number(index)]);
      Swal.fire(AAi18n.withRewards({
        icon: data.isCorrect ? "success" : "error",
        title: data.isCorrect ? loc.correctMessage : loc.wrongMessage, text,
      }, data));
      finishRound();
    } catch (error) {
      console.error("Audio answer validation failed:", error);
      Swal.fire({ icon: "error", title: loc.validationErrorTitle, text: loc.validationErrorText });
    } finally {
      validating = false;
      validateButton.disabled = false;
    }
  });
});
