(function (window, document) {
  function localizer() {
    return document.getElementById("localizer")?.dataset || {};
  }

  function message(loc, key, fallback) {
    return (loc && loc[key]) || fallback;
  }

  function labelIn(raw) {
    const button = Array.from(document.querySelectorAll(".guessAnswer, .guessQuality"))
      .find((btn) => btn.value === raw);
    if (button) return button.textContent.trim();

    const option = Array.from(document.querySelectorAll("option"))
      .find((opt) => opt.value === raw);
    return option ? option.textContent.trim() : null;
  }

  function labelFor(value) {
    const raw = String(value || "");
    if (!raw) return "";

    const label = labelIn(raw);
    if (label) return label;

    // Notes come with their octave ("C#4"); the answer buttons hold the note name only.
    const pitch = /^([A-G][#b]?)\d+$/.exec(raw);
    return (pitch && labelIn(pitch[1])) || raw;
  }

  function answerLabel(answer) {
    return String(answer || "")
      .split("|")
      .filter(Boolean)
      .map(labelFor)
      .join(" ");
  }

  function format(text, value) {
    return String(text || "").replace("{0}", value);
  }

  // Adds the XP/badge feedback from ValidateExercise (rewards.js) to dialog options.
  // A free practice round earns nothing, so its dialog says so instead (practice.js).
  function withRewards(options, data) {
    if (data && data.free && window.AAPractice) return window.AAPractice.decorate(options, data);
    return window.AARewards ? window.AARewards.decorate(options, data) : options;
  }

  window.AAi18n = {
    localizer,
    answerLabel,
    warning(title, text) {
      Swal.fire({ icon: "warning", title, text });
    },
    noAudio(loc) {
      this.warning(
        message(loc, "noAudioTitle", "No audio loaded"),
        message(loc, "noAudioText", "Click Play first to generate the exercise.")
      );
    },
    incomplete(loc) {
      this.warning(
        message(loc, "incompleteTitle", "Missing data"),
        message(loc, "incompleteText", "Generate the exercise and select your answer before validating.")
      );
    },
    serverError(data, loc) {
      if (!data || data.success !== false) return false;
      Swal.fire({
        icon: "error",
        title: message(loc, "validationErrorTitle", "Unable to validate"),
        text: data.message || message(loc, "validationErrorText", "Please generate a new exercise and try again.")
      });
      return true;
    },
    // Shows the answer dialog; returns the SweetAlert2 promise.
    result(data, loc) {
      const options = data.isCorrect
        ? {
            icon: "success",
            title: message(loc, "correctMessage", "Correct!"),
            text: message(loc, "correctMessageText", "You got it right!")
          }
        : {
            icon: "error",
            title: message(loc, "wrongMessage", "Wrong!"),
            text: format(message(loc, "wrongMessageText", "The correct answer was {0}."), answerLabel(data.answer))
          };
      return Swal.fire(withRewards(options, data));
    },
    withRewards
  };
})(window, document);
