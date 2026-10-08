// The microphone of the singing exercises (SolfegeMelody, SingNote,
// SingInterval and SingMelody), driving the controls of _MicrophoneControls:
// it records the student, shows how loudly the microphone hears them while it
// records, and finds the notes they sang with PitchDetector. Only the names of
// those notes are sent to the server; the recording never leaves the device
// (see the privacy policy).
(function (window, document) {
  "use strict";

  const MAX_RECORDING_MS = 30000;
  const SHARP_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
  const NATURAL_PITCHES = { C: 0, D: 2, E: 4, F: 5, G: 7, A: 9, B: 11 };
  // The level meter goes from -60 dBFS, about the room, to 0 dBFS.
  const METER_FLOOR_DB = -60;
  // A note this close to the pitch is in tune: about what a trained ear hears.
  const IN_TUNE_CENTS = 10;

  let recorder = null; // the MediaRecorder while recording
  let stopped = Promise.resolve();
  let recording = null; // { blob, url }
  let player = null;
  let pending = false; // asking for the microphone
  let attempt = 0; // a recording cancelled while the browser asks for the microphone doesn't start
  let meter = null; // { context, frame }
  let analyzingClosed = null;

  function texts() {
    return document.getElementById("aaSinging")?.dataset || {};
  }

  function format(text, ...values) {
    return String(text || "").replace(/\{(\d)\}/g, (match, i) => (i < values.length ? values[i] : match));
  }

  function showError(text) {
    const page = AAi18n.localizer();
    Swal.fire({ icon: "error", title: page.validationErrorTitle, text: text || page.validationErrorText });
  }

  // ---------- Notes ----------

  // "C#4" as its MIDI number, or null.
  function midi(name) {
    const match = /^([A-G])([#b]?)(-?\d+)$/.exec(String(name || "").trim());
    if (!match) return null;
    const shift = match[2] === "#" ? 1 : match[2] === "b" ? -1 : 0;
    return (parseInt(match[3], 10) + 1) * 12 + NATURAL_PITCHES[match[1]] + shift;
  }

  // The note's name in the page's language without its octave ("Dó♯"): the
  // server compares pitch classes, so a student who sings an octave lower is
  // right and must not see different notes.
  function pitchName(note) {
    const names = String(texts().noteNames || "").split("|");
    const pitchClass = ((note % 12) + 12) % 12;
    return names[pitchClass] || SHARP_NAMES[pitchClass];
  }

  // The count longest notes of those PitchDetector found, in the order they
  // were sung: a short note is more likely a slip than what the student meant.
  function longest(notes, count) {
    return notes
      .map((note, index) => ({ note, index }))
      .sort((a, b) => b.note.duration - a.note.duration)
      .slice(0, count)
      .sort((a, b) => a.index - b.index)
      .map((entry) => entry.note);
  }

  // The MIDI numbers of the notes without consecutive repeats: a held note and
  // a repeated one sound the same to the detector.
  function collapse(notes) {
    const result = [];
    for (const note of notes) {
      if (result[result.length - 1] !== note.midi) result.push(note.midi);
    }
    return result;
  }

  // How far from the pitch a note was sung, in the page's language.
  function tuning(cents) {
    const loc = texts();
    if (Math.abs(cents) <= IN_TUNE_CENTS) return loc.inTuneText;
    return format(cents > 0 ? loc.sharpText : loc.flatText, Math.abs(cents));
  }

  // ---------- Recording ----------

  function setRecordButton(active) {
    const button = document.getElementById("recordAudio");
    if (!button) return;
    const loc = texts();
    const icon = document.createElement("i");
    icon.className = active ? "bi bi-stop-circle" : "bi bi-mic";
    icon.setAttribute("aria-hidden", "true");
    button.replaceChildren(icon, ` ${active ? loc.recordStopText : loc.recordStartText}`);
    button.classList.toggle("is-recording", active);
    button.setAttribute("aria-pressed", String(active));
  }

  function startMeter(stream) {
    const element = document.getElementById("micMeter");
    const level = element?.querySelector(".aa-mic-meter-level");
    const Context = window.AudioContext || window.webkitAudioContext;
    if (!level || !Context) return;

    let context;
    try {
      context = new Context();
      const analyser = context.createAnalyser();
      analyser.fftSize = 1024;
      context.createMediaStreamSource(stream).connect(analyser);
      const samples = new Float32Array(analyser.fftSize);
      const state = { context, frame: 0 };
      let shown = 0;
      const draw = () => {
        analyser.getFloatTimeDomainData(samples);
        let sum = 0;
        for (let i = 0; i < samples.length; i++) sum += samples[i] * samples[i];
        const db = 10 * Math.log10(sum / samples.length || 1e-12);
        const fill = Math.min(1, Math.max(0, (db - METER_FLOOR_DB) / -METER_FLOOR_DB));
        // Rises at once and falls slowly, so the bar doesn't flicker.
        shown = fill > shown ? fill : shown + (fill - shown) * 0.15;
        level.style.transform = `scaleX(${shown.toFixed(3)})`;
        state.frame = window.requestAnimationFrame(draw);
      };
      context.resume?.();
      meter = state;
      element.hidden = false;
      draw();
    } catch (err) {
      // The recording doesn't need the meter.
      console.error("The level meter is not available:", err);
      context?.close?.().catch(() => {});
    }
  }

  function stopMeter() {
    const element = document.getElementById("micMeter");
    if (element) element.hidden = true;
    if (!meter) return;
    window.cancelAnimationFrame(meter.frame);
    meter.context.close().catch(() => {});
    meter = null;
  }

  function discard() {
    if (player) player.pause();
    if (recording) URL.revokeObjectURL(recording.url);
    recording = null;
  }

  async function start() {
    const loc = texts();
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === "undefined") {
      showError(loc.microphoneUnsupportedText);
      return;
    }

    const current = ++attempt;
    let stream;
    pending = true;
    try {
      // Echo cancellation, noise suppression and auto gain smear sustained
      // pitches, so ask for the raw signal when the browser allows it.
      stream = await navigator.mediaDevices.getUserMedia({
        audio: { echoCancellation: false, noiseSuppression: false, autoGainControl: false },
      });
    } catch (err) {
      console.error("Microphone access failed:", err);
      showError(loc.microphoneAccessErrorText);
      return;
    } finally {
      pending = false;
    }
    if (current !== attempt) {
      stream.getTracks().forEach((track) => track.stop());
      return;
    }

    let active;
    try {
      active = new MediaRecorder(stream);
    } catch (err) {
      console.error("MediaRecorder is not available:", err);
      stream.getTracks().forEach((track) => track.stop());
      showError(loc.microphoneUnsupportedText);
      return;
    }

    discard();
    // What the page played must not ring into the microphone (echo cancellation is off).
    AudioEngine.stop();
    const chunks = [];
    const timer = setTimeout(stop, MAX_RECORDING_MS);
    recorder = active;
    stopped = new Promise((resolve) => {
      active.addEventListener("dataavailable", (event) => {
        if (event.data && event.data.size > 0) chunks.push(event.data);
      });
      active.addEventListener("stop", () => {
        clearTimeout(timer);
        stream.getTracks().forEach((track) => track.stop());
        stopMeter();
        if (recorder === active) recorder = null;
        setRecordButton(false);
        if (chunks.length > 0) {
          // The browser picks the container (webm, ogg or mp4); keep its type so it can be decoded.
          const blob = new Blob(chunks, { type: active.mimeType || chunks[0].type });
          recording = { blob, url: URL.createObjectURL(blob) };
        }
        resolve();
      });
    });
    active.start();
    setRecordButton(true);
    startMeter(stream);
  }

  // Stops recording and keeps what was recorded; resolves once it is kept.
  function stop() {
    attempt++;
    if (recorder && recorder.state !== "inactive") recorder.stop();
    return stopped;
  }

  // Stops recording and throws the recording away: it was for another round.
  function cancel() {
    return stop().then(discard);
  }

  // ---------- Analysis ----------

  // SweetAlert2 v10 leaves aria-hidden on the page when one dialog replaces
  // another, so the "analysing" dialog is fully closed before the next one.
  function showAnalyzing() {
    analyzingClosed = new Promise((resolve) => {
      Swal.fire({
        title: texts().analyzingText,
        allowOutsideClick: false,
        allowEscapeKey: false,
        showConfirmButton: false,
        didOpen: () => Swal.showLoading(),
        didClose: resolve,
      });
    });
  }

  async function hideAnalyzing() {
    if (!analyzingClosed) return;
    const closed = analyzingClosed;
    analyzingClosed = null;
    Swal.close();
    await closed;
  }

  // The notes sung in the recording ({ midi, cents, start, duration }), with
  // the "analysing" dialog left open while the page checks them: it calls
  // hideAnalyzing() before showing the answer. Null, after a dialog saying
  // why, when there is nothing to check.
  async function transcribe() {
    await stop();
    const loc = texts();
    if (!recording) {
      AAi18n.incomplete(AAi18n.localizer());
      return null;
    }

    showAnalyzing();
    let notes;
    try {
      notes = await PitchDetector.detect(recording.blob);
    } catch (err) {
      console.error("Pitch analysis failed:", err);
      await hideAnalyzing();
      showError(loc.analysisErrorText);
      return null;
    }
    if (notes.length === 0) {
      await hideAnalyzing();
      AAi18n.warning(loc.nothingHeardTitle, loc.nothingHeardText);
      return null;
    }
    return notes;
  }

  // ---------- Answer ----------

  // The answer dialog for ValidateExercise's data, with lines of text under its title.
  function showResult(data, lines) {
    const page = AAi18n.localizer();
    const body = document.createElement("div");
    const shown = lines.filter(Boolean);
    shown.forEach((text, i) => {
      const line = document.createElement("p");
      if (i === shown.length - 1) line.className = "mb-0";
      line.textContent = text;
      body.append(line);
    });
    const options = data.isCorrect
      ? { icon: "success", title: page.correctMessage }
      : { icon: "error", title: page.wrongMessage };
    if (shown.length > 0) options.html = body;
    return Swal.fire(AAi18n.withRewards(options, data));
  }

  // ---------- Buttons ----------

  // ready(): whether there is something to sing; busy(): whether an answer is
  // being checked, when the buttons wait.
  function attach({ ready, busy }) {
    document.getElementById("recordAudio")?.addEventListener("click", () => {
      if (pending || busy()) return;
      if (recorder) {
        stop();
        return;
      }
      if (!ready()) {
        AAi18n.noAudio(AAi18n.localizer());
        return;
      }
      start();
    });

    document.getElementById("listenAudio")?.addEventListener("click", () => {
      if (!recording) {
        AAi18n.incomplete(AAi18n.localizer());
        return;
      }
      AudioEngine.stop();
      player = player || new Audio();
      player.src = recording.url;
      player.play().catch((err) => console.error("Playback failed:", err));
    });

    setRecordButton(false);
  }

  window.AASinging = {
    attach,
    stop,
    cancel,
    transcribe,
    hideAnalyzing,
    showResult,
    showError,
    isRecording: () => recorder !== null,
    hasRecording: () => recording !== null,
    midi,
    noteName: (note) => PitchDetector.noteName(note),
    pitchName,
    longest,
    collapse,
    tuning,
    format,
    texts,
  };
})(window, document);
