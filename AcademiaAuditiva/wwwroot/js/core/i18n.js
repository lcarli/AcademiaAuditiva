(function (window, document) {
  function localizer() {
    return document.getElementById("localizer")?.dataset || {};
  }

  function message(loc, key, fallback) {
    return (loc && loc[key]) || fallback;
  }

  function labelFor(value) {
    const raw = String(value || "");
    if (!raw) return "";

    const button = Array.from(document.querySelectorAll(".guessAnswer, .guessQuality"))
      .find((btn) => btn.value === raw);
    if (button) return button.textContent.trim();

    const option = Array.from(document.querySelectorAll("option"))
      .find((opt) => opt.value === raw);
    if (option) return option.textContent.trim();

    return raw;
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
    result(data, loc) {
      if (data.isCorrect) {
        Swal.fire(
          message(loc, "correctMessage", "Correct!"),
          message(loc, "correctMessageText", "You got it right!"),
          "success"
        );
        return;
      }

      const answer = answerLabel(data.answer);
      Swal.fire(
        message(loc, "wrongMessage", "Wrong!"),
        format(message(loc, "wrongMessageText", "The correct answer was {0}."), answer),
        "error"
      );
    }
  };
})(window, document);
