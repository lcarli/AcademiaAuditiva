// Sight-singing: the student reads a short melody, hears its first note,
// records themselves singing it, and the recording is transcribed in the
// browser with essentia.js (PitchMelodia + PitchContourSegmentation). Only
// the detected note names are sent to the server; the audio never leaves
// the device (see the privacy policy).
document.addEventListener("DOMContentLoaded", () => {
  const loc = AAi18n.localizer();
  const exerciseId = document.getElementById("exerciseId")?.value;
  const sheet = document.getElementById("output-sheet");
  const generateBtn = document.getElementById("Generate");
  const startingNoteBtn = document.getElementById("playStartingNote");
  const recordBtn = document.getElementById("recordAudio");
  const listenBtn = document.getElementById("listenAudio");
  const validateBtn = document.getElementById("validateGuess");

  const SHARP_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
  const NATURAL_PITCHES = { C: 0, D: 2, E: 4, F: 5, G: 7, A: 9, B: 11 };
  const DURATIONS = { 4: "w", 2: "h", 1: "q", 0.5: "8", 0.25: "16" };
  const localNoteNames = String(loc.noteNames || "").split("|");

  const ANALYSIS_RATE = 44100;
  const HOP_SIZE = 128;
  const MIN_FREQUENCY = 70; // below a bass's low E2
  const MAX_FREQUENCY = 1200; // above a soprano's C6
  const MIN_NOTE_SECONDS = 0.12;
  const MAX_RECORDING_MS = 30000;
  // Frames quieter than about -50 dBFS count as silence: PitchMelodia fails
  // on digital silence (a muted microphone) and reads pitches into room noise.
  const SILENCE_RMS = 0.003;
  const RMS_WINDOW = 1024;

  let round = null; // { melody, startedAt }
  let recorder = null;
  let recorderStopped = Promise.resolve();
  let recording = null; // { blob, url }
  let player = null;
  let audioContext = null;
  let essentiaReady = null;
  let micPending = false;
  let busy = false;

  function parseNote(name) {
    const match = /^([A-G])([#b]?)(-?\d+)$/.exec(String(name || "").trim());
    if (!match) return null;
    const octave = parseInt(match[3], 10);
    const shift = match[2] === "#" ? 1 : match[2] === "b" ? -1 : 0;
    return {
      letter: match[1],
      accidental: match[2],
      octave,
      midi: (octave + 1) * 12 + NATURAL_PITCHES[match[1]] + shift,
    };
  }

  function midiToName(midi) {
    return SHARP_NAMES[midi % 12] + (Math.floor(midi / 12) - 1);
  }

  // Octaves are left out: the server compares pitch classes, so a student
  // who sings an octave lower is right and must not see different notes.
  function displayName(midi) {
    const pitchClass = ((midi % 12) + 12) % 12;
    return localNoteNames[pitchClass] || SHARP_NAMES[pitchClass];
  }

  function melodyNotes(melody) {
    return melody
      .filter((item) => item.type === "note")
      .map((item) => parseNote(item.note))
      .filter(Boolean);
  }

  function fill(text, value) {
    return String(text || "{0}").replace("{0}", () => value);
  }

  function showError(text) {
    Swal.fire({ icon: "error", title: loc.validationErrorTitle, text: text || loc.validationErrorText });
  }

  // ---------- Sheet music ----------

  function drawStaff(melody) {
    const VF = Vex.Flow;
    sheet.replaceChildren();
    const renderer = new VF.Renderer(sheet, VF.Renderer.Backends.SVG);
    renderer.resize(500, 150);
    const context = renderer.getContext();
    const stave = new VF.Stave(10, 30, 480).addClef("treble");
    if (melody.length > 0) stave.addTimeSignature("4/4");
    stave.setContext(context).draw();

    const names = melodyNotes(melody).map((note) => displayName(note.midi));
    if (names.length === 0) {
      sheet.removeAttribute("role");
      sheet.removeAttribute("aria-label");
      return;
    }

    const tickables = melody.map((item) => {
      const duration = DURATIONS[item.duration] || "q";
      const note = item.type === "note" ? parseNote(item.note) : null;
      if (!note) {
        return new VF.StaveNote({ clef: "treble", keys: ["b/4"], duration: duration + "r" });
      }
      const staveNote = new VF.StaveNote({
        clef: "treble",
        keys: [`${note.letter.toLowerCase()}${note.accidental}/${note.octave}`],
        duration,
      });
      if (note.accidental) staveNote.addModifier(new VF.Accidental(note.accidental), 0);
      return staveNote;
    });

    const voice = new VF.Voice({ num_beats: 4, beat_value: 4 }).setMode(VF.Voice.Mode.SOFT);
    voice.addTickables(tickables);
    new VF.Formatter().joinVoices([voice]).format([voice], 400);
    voice.draw(context, stave);

    sheet.setAttribute("role", "img");
    sheet.setAttribute("aria-label", names.join(", "));
  }

  // ---------- Starting note ----------

  // Create or resume the context while handling the click: Safari only lets
  // audio start from a user gesture, and the melody arrives after a fetch.
  function unlockAudio() {
    const Context = window.AudioContext || window.webkitAudioContext;
    if (!Context) return null;
    if (!audioContext) audioContext = new Context();
    if (audioContext.state === "suspended") audioContext.resume();
    return audioContext;
  }

  function playStartingNote() {
    const first = round ? melodyNotes(round.melody)[0] : null;
    if (!first) {
      AAi18n.noAudio(loc);
      return;
    }
    const context = unlockAudio();
    if (!context) return;

    const start = context.currentTime + 0.05;
    const oscillator = context.createOscillator();
    const gain = context.createGain();
    oscillator.type = "triangle";
    oscillator.frequency.value = 440 * Math.pow(2, (first.midi - 69) / 12);
    gain.gain.setValueAtTime(0.0001, start);
    gain.gain.exponentialRampToValueAtTime(0.3, start + 0.04);
    gain.gain.setValueAtTime(0.3, start + 1.1);
    gain.gain.exponentialRampToValueAtTime(0.0001, start + 1.5);
    oscillator.connect(gain);
    gain.connect(context.destination);
    oscillator.start(start);
    oscillator.stop(start + 1.55);
  }

  // ---------- Recording ----------

  function setRecordButton(active) {
    if (!recordBtn) return;
    const icon = document.createElement("i");
    icon.className = active ? "bi bi-stop-circle" : "bi bi-mic";
    icon.setAttribute("aria-hidden", "true");
    recordBtn.replaceChildren(icon, ` ${active ? loc.recordStopText : loc.recordStartText}`);
    recordBtn.classList.toggle("is-recording", active);
    recordBtn.setAttribute("aria-pressed", String(active));
  }

  function discardRecording() {
    if (player) player.pause();
    if (recording) URL.revokeObjectURL(recording.url);
    recording = null;
  }

  async function startRecording() {
    if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === "undefined") {
      showError(loc.microphoneUnsupportedText);
      return;
    }

    let stream;
    micPending = true;
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
      micPending = false;
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

    discardRecording();
    const chunks = [];
    const timer = setTimeout(stopRecording, MAX_RECORDING_MS);
    recorder = active;
    recorderStopped = new Promise((resolve) => {
      active.addEventListener("dataavailable", (event) => {
        if (event.data && event.data.size > 0) chunks.push(event.data);
      });
      active.addEventListener("stop", () => {
        clearTimeout(timer);
        stream.getTracks().forEach((track) => track.stop());
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
  }

  function stopRecording() {
    if (recorder && recorder.state !== "inactive") recorder.stop();
    return recorderStopped;
  }

  // ---------- Pitch analysis ----------

  // essentia-wasm.web.js (loaded by the view) compiles its .wasm file
  // asynchronously and resolves with the module.
  function loadEssentia() {
    if (!essentiaReady) {
      if (typeof EssentiaWASM !== "function" || typeof Essentia !== "function") {
        return Promise.reject(new Error("essentia.js is not loaded."));
      }
      essentiaReady = EssentiaWASM().then((wasm) => new Essentia(wasm));
      essentiaReady.catch(() => {
        essentiaReady = null;
      });
    }
    return essentiaReady;
  }

  async function decodeMono(blob) {
    // decodeAudioData resamples to the context's rate, which the analysis assumes.
    const context = new OfflineAudioContext(1, 1, ANALYSIS_RATE);
    const buffer = await context.decodeAudioData(await blob.arrayBuffer());
    const mono = new Float32Array(buffer.length);
    for (let channel = 0; channel < buffer.numberOfChannels; channel++) {
      const data = buffer.getChannelData(channel);
      for (let i = 0; i < data.length; i++) mono[i] += data[i] / buffer.numberOfChannels;
    }
    return mono;
  }

  // RMS around each analysis frame; PitchMelodia centres frame i on sample i * HOP_SIZE.
  function frameLevels(samples) {
    const frames = Math.ceil(samples.length / HOP_SIZE) + 2;
    const levels = new Float32Array(frames);
    const half = RMS_WINDOW / 2;
    for (let i = 0; i < frames; i++) {
      const from = Math.max(0, i * HOP_SIZE - half);
      const to = Math.min(samples.length, i * HOP_SIZE + half);
      let sum = 0;
      for (let j = from; j < to; j++) sum += samples[j] * samples[j];
      levels[i] = to > from ? Math.sqrt(sum / (to - from)) : 0;
    }
    return levels;
  }

  // MIDI numbers of the sung notes, without consecutive repeats (a held
  // note and a repeated one sound the same to the tracker).
  async function detectNotes(blob) {
    const [essentia, samples] = await Promise.all([loadEssentia(), decodeMono(blob)]);
    const levels = frameLevels(samples);
    if (!levels.some((level) => level >= SILENCE_RMS)) return [];

    const vectors = [];
    try {
      const signal = essentia.arrayToVector(samples);
      vectors.push(signal);
      // Positional arguments follow essentia.js's alphabetical order:
      // binResolution, filterIterations, frameSize, guessUnvoiced,
      // harmonicWeight, hopSize, magnitudeCompression, magnitudeThreshold,
      // maxFrequency, minDuration, minFrequency, numberHarmonics,
      // peakDistributionThreshold, peakFrameThreshold, pitchContinuity,
      // referenceFrequency, sampleRate, timeContinuity.
      const melodia = essentia.PitchMelodia(
        signal, 10, 3, 2048, false, 0.8, HOP_SIZE, 1, 40, MAX_FREQUENCY, 100, MIN_FREQUENCY,
        20, 0.9, 0.9, 27.5625, 55, ANALYSIS_RATE, 100);
      vectors.push(melodia.pitch, melodia.pitchConfidence);

      const pitch = essentia.vectorToArray(melodia.pitch);
      let voiced = 0;
      for (let i = 0; i < pitch.length; i++) {
        if (pitch[i] > 0 && (levels[i] ?? 0) >= SILENCE_RMS) voiced++;
        else pitch[i] = 0;
      }
      // PitchContourSegmentation throws on a contour without voiced frames.
      if (voiced === 0) return [];
      const contour = essentia.arrayToVector(pitch);
      vectors.push(contour);

      // hopSize, minDuration, pitchDistanceThreshold (cents), rmsThreshold, sampleRate, tuningFrequency.
      const segments = essentia.PitchContourSegmentation(
        contour, signal, HOP_SIZE, MIN_NOTE_SECONDS, 60, -2, ANALYSIS_RATE, 440);
      vectors.push(segments.onset, segments.duration, segments.MIDIpitch);

      const notes = [];
      for (const value of essentia.vectorToArray(segments.MIDIpitch)) {
        const midi = Math.round(value);
        if (midi < 24 || midi > 108) continue;
        if (notes[notes.length - 1] !== midi) notes.push(midi);
      }
      return notes;
    } finally {
      vectors.forEach((vector) => vector?.delete?.());
    }
  }

  // ---------- Result ----------

  // SweetAlert2 v10 leaves aria-hidden on the page when one dialog replaces
  // another, so the "analysing" dialog is fully closed before the next one.
  let analyzingClosed = null;

  function showAnalyzing() {
    analyzingClosed = new Promise((resolve) => {
      Swal.fire({
        title: loc.analyzingText,
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

  function showWrongAnswer(data, sung) {
    const expected = String(data.answer || "").split("|").map(parseNote).filter(Boolean);
    const correct = document.createElement("p");
    correct.textContent = fill(loc.wrongMessageText, expected.map((note) => displayName(note.midi)).join(" "));
    const heard = document.createElement("p");
    heard.className = "mb-0";
    heard.textContent = fill(loc.heardText, sung.map(displayName).join(" "));
    const body = document.createElement("div");
    body.append(correct, heard);
    Swal.fire(AAi18n.withRewards({ icon: "error", title: loc.wrongMessage, html: body }, data));
  }

  // ---------- Buttons ----------

  generateBtn?.addEventListener("click", async () => {
    if (!exerciseId || busy) return;
    unlockAudio();
    generateBtn.disabled = true;
    try {
      const response = await fetch("/Exercise/RequestPlay", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ exerciseId }),
      });
      const data = await response.json();
      if (AAi18n.serverError(data, loc)) return;
      if (!Array.isArray(data.melody) || melodyNotes(data.melody).length === 0) {
        throw new Error("The response has no melody.");
      }

      await stopRecording();
      discardRecording();
      round = { melody: data.melody, startedAt: Date.now() };
      drawStaff(round.melody);
      playStartingNote();
    } catch (err) {
      console.error("SolfegeMelody request failed:", err);
      showError();
    } finally {
      generateBtn.disabled = false;
    }
  });

  startingNoteBtn?.addEventListener("click", () => playStartingNote());

  recordBtn?.addEventListener("click", () => {
    if (micPending || busy) return;
    if (recorder) {
      stopRecording();
      return;
    }
    if (!round) {
      AAi18n.noAudio(loc);
      return;
    }
    startRecording();
  });

  listenBtn?.addEventListener("click", () => {
    if (!recording) {
      AAi18n.incomplete(loc);
      return;
    }
    player = player || new Audio();
    player.src = recording.url;
    player.play().catch((err) => console.error("Playback failed:", err));
  });

  validateBtn?.addEventListener("click", async () => {
    if (busy) return;
    if (!round) {
      AAi18n.noAudio(loc);
      return;
    }

    busy = true;
    validateBtn.disabled = true;
    try {
      await stopRecording();
      if (!recording) {
        AAi18n.incomplete(loc);
        return;
      }

      showAnalyzing();
      let sung;
      try {
        sung = await detectNotes(recording.blob);
      } catch (err) {
        console.error("Pitch analysis failed:", err);
        await hideAnalyzing();
        showError(loc.analysisErrorText);
        return;
      }
      if (sung.length === 0) {
        await hideAnalyzing();
        Swal.fire({ icon: "warning", title: loc.nothingHeardTitle, text: loc.nothingHeardText });
        return;
      }

      const response = await fetch("/Exercise/ValidateExercise", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          exerciseId,
          userGuess: sung.map(midiToName).join("|"),
          timeSpentSeconds: Math.floor((Date.now() - round.startedAt) / 1000),
        }),
      });
      const data = await response.json();
      // The server forgets the expected answer after one attempt.
      round = null;
      await hideAnalyzing();
      if (AAi18n.serverError(data, loc)) return;

      const counter = document.getElementById(data.isCorrect ? "correctCount" : "errorCount");
      if (counter) counter.innerText = parseInt(counter.innerText, 10) + 1;

      if (data.isCorrect) {
        AAi18n.result(data, loc);
      } else {
        showWrongAnswer(data, sung);
      }
    } catch (err) {
      console.error("SolfegeMelody validation failed:", err);
      await hideAnalyzing();
      showError();
    } finally {
      busy = false;
      validateBtn.disabled = false;
    }
  });

  drawStaff([]);
  setRecordButton(false);
});
