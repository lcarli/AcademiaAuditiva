document.addEventListener("DOMContentLoaded", () => {
  AcademiaAuditiva.init();
  const loc = AAi18n.localizer();
  const exerciseId = document.getElementById("exerciseId")?.value;
  const levelSelect = document.getElementById(loc.levelFilter);
  const ab = ABPlayback.create(document.querySelector("[data-aa-ab]"));
  const frequency = document.getElementById("eqFrequency");
  const gain = document.getElementById("eqGain");
  const q = document.getElementById("eqQ");
  const preview = document.getElementById("previewEq");
  const previewStatus = document.getElementById("eqPreviewStatus");
  const validateButton = document.getElementById("validateGuess");
  let roundId = null;
  let sourceToken = null;
  let validating = false;

  const number = value => new Intl.NumberFormat(document.documentElement.lang, { maximumFractionDigits: 1 }).format(value);
  const frequencyFor = slider => 60 * Math.pow(12000 / 60, Number(slider.value) / 1000);
  const parameters = () => ({ frequencyHz: frequencyFor(frequency), gainDb: Number(gain.value), q: Number(q.value) });

  function updateCurve({ frequencyHz, gainDb, q }) {
    // Web Audio's peaking response is the same filter used for the audition.
    const sampleRate = 44100;
    const omega = 2 * Math.PI * frequencyHz / sampleRate;
    const amplitude = Math.pow(10, gainDb / 40);
    const alpha = Math.sin(omega) / (2 * q);
    const b = [1 + alpha * amplitude, -2 * Math.cos(omega), 1 - alpha * amplitude];
    const a = [1 + alpha / amplitude, -2 * Math.cos(omega), 1 - alpha / amplitude];
    const points = Array.from({ length: 181 }, (_, index) => {
      const hz = 60 * Math.pow(200, index / 180);
      const w = 2 * Math.PI * hz / sampleRate;
      const power = coefficients => Math.pow(coefficients[0] + coefficients[1] * Math.cos(w) + coefficients[2] * Math.cos(2 * w), 2)
        + Math.pow(coefficients[1] * Math.sin(w) + coefficients[2] * Math.sin(2 * w), 2);
      const db = 10 * Math.log10(power(b) / power(a));
      return `${(40 + index * 3).toFixed(2)},${(90 - db * 74 / 12).toFixed(2)}`;
    });
    document.getElementById("eqResponseCurve").setAttribute("d", `M${points.join(" L")}`);
    const point = document.getElementById("eqResponsePoint");
    point.setAttribute("cx", 40 + Math.log(frequencyHz / 60) / Math.log(200) * 540);
    point.setAttribute("cy", 90 - gainDb * 74 / 12);
  }

  function updateLabels() {
    const values = parameters();
    document.getElementById("eqFrequencyValue").textContent = loc.frequencyLabel.replace("{0}", number(values.frequencyHz));
    document.getElementById("eqGainValue").textContent = loc.gainLabel.replace("{0}", number(values.gainDb));
    document.getElementById("eqQValue").textContent = number(values.q);
    [frequency, gain, q].forEach(control => {
      const fraction = (Number(control.value) - Number(control.min)) / (Number(control.max) - Number(control.min));
      const knob = control.closest("[data-eq-knob]");
      knob.style.setProperty("--knob-rotation", `${-135 + fraction * 270}deg`);
      knob.style.setProperty("--knob-progress", fraction * 100);
      control.setAttribute("aria-valuetext", document.getElementById(`${control.id}Value`).textContent);
    });
    updateCurve(values);
  }

  [frequency, gain, q].forEach(control => {
    let drag = null;
    const knob = control.closest("[data-eq-knob]");
    control.addEventListener("pointerdown", event => {
      if (control.disabled || event.button !== 0 || drag) return;
      event.preventDefault();
      control.focus();
      control.setPointerCapture(event.pointerId);
      drag = { pointerId: event.pointerId, x: event.clientX, y: event.clientY, value: Number(control.value) };
      knob.dataset.dragging = "";
    });
    control.addEventListener("pointermove", event => {
      if (!drag || event.pointerId !== drag.pointerId || control.disabled) return;
      const delta = drag.y - event.clientY + event.clientX - drag.x;
      const step = Number(control.step);
      const span = Number(control.max) - Number(control.min);
      drag.value = Math.min(Number(control.max), Math.max(Number(control.min),
        drag.value + delta / (event.shiftKey ? 1600 : 240) * span));
      drag.x = event.clientX;
      drag.y = event.clientY;
      control.value = String(Math.min(Number(control.max), Math.max(Number(control.min),
        Number(control.min) + Math.round((drag.value - Number(control.min)) / step) * step)));
      control.dispatchEvent(new Event("input", { bubbles: true }));
    });
    const endDrag = event => {
      if (!drag || event.pointerId !== drag.pointerId) return;
      drag = null;
      delete knob.dataset.dragging;
    };
    control.addEventListener("pointerup", endDrag);
    control.addEventListener("pointercancel", endDrag);
    control.addEventListener("lostpointercapture", endDrag);
  });

  function setControlsEnabled(enabled) {
    [frequency, gain, q, preview].forEach(control => { control.disabled = !enabled; });
    validateButton.disabled = !enabled;
  }

  function clearRound() {
    roundId = null;
    sourceToken = null;
    previewStatus.textContent = "";
    setControlsEnabled(false);
    ab?.clear();
  }

  [frequency, gain, q].forEach(control => control.addEventListener("input", updateLabels));
  levelSelect?.addEventListener("change", () => AAPractice.reset());
  AAPractice.onReset(clearRound);
  updateLabels();

  document.getElementById("Play")?.addEventListener("click", () => {
    AAPractice.play({
      exerciseId, filters: { [loc.levelFilter]: levelSelect?.value || "beginner" },
    }).then(data => {
      if (AAi18n.serverError(data, loc)) return;
      try {
        ab.setRound(data.clips);
      } catch (error) {
        console.error("Invalid EQ matching round:", error);
        AAPractice.reset();
        Swal.fire({ icon: "error", title: loc.validationErrorTitle, text: loc.validationErrorText });
        return;
      }
      sourceToken = data.clips.find(clip => clip.key === "A")?.token;
      if (!sourceToken) {
        AAPractice.reset();
        Swal.fire({ icon: "error", title: loc.validationErrorTitle, text: loc.validationErrorText });
        return;
      }
      roundId = data.roundId;
      previewStatus.textContent = "";
      setControlsEnabled(true);
      ab.play("A");
    });
  });

  preview.addEventListener("click", async () => {
    if (!sourceToken) return;
    preview.disabled = true;
    try {
      await AudioEngine.playTokenWithEq(sourceToken, parameters());
    } catch (error) {
      console.error("Could not audition the EQ settings:", error);
      previewStatus.textContent = loc.previewError;
      Swal.fire({ icon: "error", title: loc.validationErrorTitle, text: loc.previewError });
    } finally {
      preview.disabled = !sourceToken;
    }
  });

  validateButton.addEventListener("click", async () => {
    if (validating) return;
    if (!roundId) {
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
      const guess = parameters();
      const data = await AAPractice.validate({
        exerciseId,
        roundId,
        userGuess: `${guess.frequencyHz}|${guess.gainDb}|${guess.q}`,
      });
      if (AAi18n.serverError(data, loc)) return;

      const detail = data.detail;
      const values = [
        Math.round(detail.score * 100),
        number(detail.targetFrequencyHz),
        number(detail.targetGainDb),
        number(detail.targetQ),
        number(guess.frequencyHz),
        number(guess.gainDb),
        number(guess.q),
      ];
      const text = loc.feedback.replace(/\{(\d+)\}/g, (_, index) => values[Number(index)]);
      Swal.fire(AAi18n.withRewards({
        icon: data.isCorrect ? "success" : "error",
        title: data.isCorrect ? loc.correctMessage : loc.wrongMessage,
        text,
      }, data));
      roundId = null;
    } catch (error) {
      console.error("EQ matching validation failed:", error);
      Swal.fire({ icon: "error", title: loc.validationErrorTitle, text: loc.validationErrorText });
    } finally {
      validating = false;
      validateButton.disabled = !sourceToken;
    }
  });
});
