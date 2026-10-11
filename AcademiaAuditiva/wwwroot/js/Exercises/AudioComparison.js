document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  const loc = AAi18n.localizer();
  const exerciseId = document.getElementById("exerciseId")?.value;
  const levelSelect = document.getElementById(loc.levelFilter);
  const ab = ABPlayback.create(document.querySelector("[data-aa-ab]"));
  const buttons = [...document.querySelectorAll(".guessAnswer")];
  let roundId = null;
  let answer = "";

  function select(selected) {
    answer = selected?.value || "";
    buttons.forEach(button => {
      button.classList.toggle("selected", button === selected);
      button.setAttribute("aria-pressed", button === selected ? "true" : "false");
    });
  }

  function clearRound() {
    roundId = null;
    select(null);
    ab?.clear();
  }

  buttons.forEach(button => button.addEventListener("click", () => select(button)));
  levelSelect?.addEventListener("change", () => AAPractice.reset());
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
      select(null);
      ab.play("A");
    });
  });

  document.getElementById("validateGuess")?.addEventListener("click", () => {
    if (!roundId || !answer) {
      AAi18n.incomplete(loc);
      return;
    }
    AAPractice.validate({ exerciseId, roundId, userGuess: answer }).then(data => {
      if (AAi18n.serverError(data, loc)) return;
      const detail = data.detail;
      const number = value => new Intl.NumberFormat(document.documentElement.lang).format(value);
      const values = loc.exercise === "BoostOrCut"
        ? [loc[detail.direction], loc[detail.region], number(detail.frequencyHz), number(detail.gainDb)]
        : loc.exercise === "GuessQ"
          ? [loc[detail.width], number(detail.frequencyHz), number(detail.q)]
          : loc.exercise === "Compression"
            ? [detail.compressedClip, number(detail.ratio), number(detail.attackMs), number(detail.releaseMs)]
            : [buttons.find(button => button.value === detail.answer).textContent.trim(), ...detail.parameters.map(number)];
      const text = loc.feedback.replace(/\{(\d+)\}/g, (_, index) => values[Number(index)]);
      Swal.fire(AAi18n.withRewards({
        icon: data.isCorrect ? "success" : "error",
        title: data.isCorrect ? loc.correctMessage : loc.wrongMessage, text,
      }, data));
      clearRound();
    });
  });
});
