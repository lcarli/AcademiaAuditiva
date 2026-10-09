document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();

  const loc = AAi18n.localizer();
  const exerciseId = document.getElementById("exerciseId")?.value;
  const levelSelect = document.getElementById("gfLevel");
  const ab = ABPlayback.create(document.querySelector("[data-aa-ab]"));
  const buttons = [...document.querySelectorAll(".guessAnswer")];
  let roundId = null;
  let selectedFrequency = "";

  function level() {
    return levelSelect?.value || "beginner";
  }

  function select(selected) {
    selectedFrequency = selected?.value || "";
    buttons.forEach((button) => {
      const active = button === selected;
      button.classList.toggle("selected", active);
      button.setAttribute("aria-pressed", active ? "true" : "false");
    });
  }

  function clearRound() {
    roundId = null;
    select(null);
    ab?.clear();
  }

  function showLevel() {
    buttons.forEach((button) => {
      button.hidden = !button.dataset.levels.split(" ").includes(level());
    });
  }

  buttons.forEach((button) => {
    button.setAttribute("aria-pressed", "false");
    button.addEventListener("click", () => select(button));
  });
  levelSelect?.addEventListener("change", () => {
    AAPractice.reset();
    showLevel();
  });
  AAPractice.onReset(clearRound);
  showLevel();

  document.getElementById("Play")?.addEventListener("click", () => {
    AAPractice.play({ exerciseId, filters: { gfLevel: level() } }).then((data) => {
      if (AAi18n.serverError(data, loc)) return;
      try {
        ab.setRound(data.clips);
      } catch (error) {
        console.error("Invalid A/B round:", error);
        AAPractice.reset();
        Swal.fire({
          icon: "error",
          title: loc.validationErrorTitle,
          text: loc.validationErrorText,
        });
        return;
      }
      roundId = data.roundId;
      select(null);
      ab.play("A");
    });
  });

  document.getElementById("validateGuess")?.addEventListener("click", () => {
    if (!roundId || !selectedFrequency) {
      AAi18n.incomplete(loc);
      return;
    }

    AAPractice.validate({ exerciseId, roundId, userGuess: selectedFrequency }).then((data) => {
      if (AAi18n.serverError(data, loc)) return;

      const detail = data.detail;
      const regionKey = `region${detail.region[0].toUpperCase()}${detail.region.slice(1)}`;
      const cueKey = `cue${detail.sourceKind[0].toUpperCase()}${detail.sourceKind.slice(1)}`;
      const cue = loc[cueKey];
      const frequency = new Intl.NumberFormat(document.documentElement.lang).format(detail.frequencyHz);
      const text = loc.feedback.replace("{0}", detail.boostedClip)
        .replace("{1}", frequency).replace("{2}", loc[regionKey]).replace("{3}", cue);
      Swal.fire(AAi18n.withRewards({
        icon: data.isCorrect ? "success" : "error",
        title: data.isCorrect ? loc.correctMessage : loc.wrongMessage,
        text,
      }, data));
      clearRound();
    });
  });
});
