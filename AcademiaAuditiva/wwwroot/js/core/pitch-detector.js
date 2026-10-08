// Finds the notes sung in a recording, in the browser. YIN (de Cheveigné and
// Kawahara, 2002) finds the pitch of every 10 ms frame, and the frames are then
// grouped into notes where the pitch holds steady, so that vibrato, a slide
// into a note or a breath in the middle of one doesn't count as another note.
// Written for Academia Auditiva and MIT licensed like the rest of the site.
(function (window) {
  "use strict";

  // The voice's fundamentals are below 1.2 kHz, so 16 kHz is plenty, and four
  // times faster to search than 44.1 kHz.
  const SAMPLE_RATE = 16000;
  const HOP = 160; // 10 ms between frames
  const WINDOW = 512; // 32 ms compared with itself
  const MIN_FREQUENCY = 70; // below a bass's low E2
  const MAX_FREQUENCY = 1200; // above a soprano's C6
  const TAU_MIN = Math.floor(SAMPLE_RATE / MAX_FREQUENCY);
  const TAU_MAX = Math.ceil(SAMPLE_RATE / MIN_FREQUENCY);

  const DEFAULTS = {
    // YIN's absolute threshold, and the most aperiodic frame still taken as sung.
    threshold: 0.15,
    maxAperiodicity: 0.35,
    // Frames quieter than about -56 dBFS, or 30 dB below the loudest one, are
    // silence or the room, not singing.
    silenceRms: 0.0015,
    relativeSilence: 0.03,
    // Each frame's pitch is the median of this many around it, which takes out
    // vibrato (a cycle lasts 14 to 22 frames).
    smoothFrames: 19,
    // A note changes where the frames after and the frames before differ by at
    // least this many semitones.
    compareFrames: 18,
    minStep: 0.5,
    // Unvoiced frames a note may hold without ending (a consonant, a breath).
    maxGapFrames: 8,
    // Shorter notes are slides between notes, or noise.
    minNoteFrames: 10,
    // A short note just under or over the next one, as the voice starts, is
    // the slide into that note.
    scoopFrames: 20,
    scoopSemitones: 3,
  };

  // ---------- Pitch of each frame ----------

  // The lag of the period in x[start ...], or 0 for an unvoiced frame.
  function yinLag(x, start, d, cmnd, options) {
    for (let tau = 1; tau <= TAU_MAX; tau++) {
      let sum = 0;
      for (let j = 0; j < WINDOW; j++) {
        const diff = x[start + j] - x[start + j + tau];
        sum += diff * diff;
      }
      d[tau] = sum;
    }

    // The cumulative mean normalized difference: near 0 at the period.
    cmnd[0] = 1;
    let running = 0;
    for (let tau = 1; tau <= TAU_MAX; tau++) {
      running += d[tau];
      cmnd[tau] = running > 0 ? (d[tau] * tau) / running : 1;
    }

    // The first dip under the threshold, to the bottom of it: the shortest
    // period that fits, which keeps the octave below out.
    let tau = -1;
    for (let t = TAU_MIN; t <= TAU_MAX; t++) {
      if (cmnd[t] < options.threshold) {
        while (t + 1 <= TAU_MAX && cmnd[t + 1] < cmnd[t]) t++;
        tau = t;
        break;
      }
    }
    if (tau < 0) {
      tau = TAU_MIN;
      for (let t = TAU_MIN + 1; t <= TAU_MAX; t++) if (cmnd[t] < cmnd[tau]) tau = t;
    }
    if (cmnd[tau] > options.maxAperiodicity || tau >= TAU_MAX) return 0;

    // Between samples, at the bottom of the parabola through the dip.
    const a = cmnd[tau - 1];
    const b = cmnd[tau];
    const c = cmnd[tau + 1];
    const curve = a - 2 * b + c;
    return curve > 0 ? tau + (0.5 * (a - c)) / curve : tau;
  }

  // The pitch of each frame as a MIDI number with cents (NaN where nothing is sung).
  function frames(samples, settings) {
    const options = Object.assign({}, DEFAULTS, settings);
    const count = Math.max(0, Math.floor((samples.length - WINDOW - TAU_MAX - 1) / HOP) + 1);
    const levels = new Float32Array(count);
    let loudest = 0;
    for (let i = 0; i < count; i++) {
      let sum = 0;
      const start = i * HOP;
      for (let j = 0; j < WINDOW; j++) sum += samples[start + j] * samples[start + j];
      levels[i] = Math.sqrt(sum / WINDOW);
      if (levels[i] > loudest) loudest = levels[i];
    }

    const quiet = Math.max(options.silenceRms, loudest * options.relativeSilence);
    const midi = new Float32Array(count).fill(NaN);
    const d = new Float32Array(TAU_MAX + 1);
    const cmnd = new Float32Array(TAU_MAX + 1);
    for (let i = 0; i < count; i++) {
      if (levels[i] < quiet) continue;
      const lag = yinLag(samples, i * HOP, d, cmnd, options);
      if (lag > 0) midi[i] = 69 + 12 * Math.log2(SAMPLE_RATE / lag / 440);
    }
    return { midi, levels };
  }

  // ---------- Frames into notes ----------

  function median(values) {
    const sorted = Array.from(values).sort((a, b) => a - b);
    const middle = sorted.length >> 1;
    return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
  }

  // A frame an octave away from its neighbours is the tracker's mistake, not
  // the singer's: it is moved back to their octave.
  function fixOctaves(midi) {
    const fixed = Float32Array.from(midi);
    for (let i = 0; i < midi.length; i++) {
      if (Number.isNaN(midi[i])) continue;
      const around = [];
      for (let j = Math.max(0, i - 8); j <= Math.min(midi.length - 1, i + 8); j++) {
        if (j !== i && !Number.isNaN(midi[j])) around.push(midi[j]);
      }
      if (around.length < 4) continue;
      const usual = median(around);
      const off = midi[i] - usual;
      if (Math.abs(Math.abs(off) - 12) < 1.5) fixed[i] = midi[i] - 12 * Math.sign(off);
    }
    return fixed;
  }

  // Each frame's pitch as the median of the frames around it, which takes out
  // vibrato (a cycle lasts 14 to 22 frames) and keeps the steps between notes.
  function smooth(midi, frames) {
    const half = frames >> 1;
    const out = new Float32Array(midi.length).fill(NaN);
    for (let i = 0; i < midi.length; i++) {
      if (Number.isNaN(midi[i])) continue;
      const around = [];
      for (let j = Math.max(0, i - half); j <= Math.min(midi.length - 1, i + half); j++) {
        if (!Number.isNaN(midi[j])) around.push(midi[j]);
      }
      out[i] = median(around);
    }
    return out;
  }

  // The stretches sung without stopping for longer than a breath, as lists of
  // their voiced frames.
  function phrases(contour, maxGap) {
    const out = [];
    let current = null;
    for (let i = 0; i < contour.length; i++) {
      if (Number.isNaN(contour[i])) continue;
      if (!current || i - current[current.length - 1] - 1 > maxGap) out.push((current = []));
      current.push(i);
    }
    return out;
  }

  // Where the notes of a phrase change: the places at which the pitch of the
  // frames after differs most from that of the frames before, by enough. A slow
  // drift, or vibrato left over, doesn't differ enough in so few frames.
  function changes(values, options) {
    const k = options.compareFrames;
    const least = k >> 1;
    const step = new Float32Array(values.length);
    for (let p = least; p <= values.length - least; p++) {
      step[p] = median(values.slice(p, p + k)) - median(values.slice(Math.max(0, p - k), p));
    }
    const out = [];
    for (let p = least; p <= values.length - least; p++) {
      const size = Math.abs(step[p]);
      if (size < options.minStep) continue;
      let peak = true;
      for (let q = Math.max(0, p - least); q <= Math.min(values.length - 1, p + least) && peak; q++) {
        const other = Math.abs(step[q]);
        if (other > size || (other === size && q < p)) peak = false;
      }
      if (peak) out.push(p);
    }
    return out;
  }

  // The notes in a contour of frames: { midi, cents, start, duration }, in seconds.
  function segment(midi, settings) {
    const options = Object.assign({}, DEFAULTS, settings);
    const contour = smooth(midi, options.smoothFrames);
    const span = (part) => part.frames[part.frames.length - 1] - part.first + 1;
    // The note's pitch is that of the frames themselves, vibrato and all.
    const pitch = (part) => median(part.frames.map((i) => midi[i]));
    const notes = [];

    for (const frames of phrases(contour, options.maxGapFrames)) {
      const values = frames.map((i) => contour[i]);
      const cuts = [0, ...changes(values, options), frames.length];
      const parts = [];
      for (let c = 0; c + 1 < cuts.length; c++) {
        const part = frames.slice(cuts[c], cuts[c + 1]);
        parts.push({ first: part[0], frames: part });
      }

      // Sliding into the first note, or falling off the last one.
      const close = (a, b) => Math.abs(pitch(a) - pitch(b)) <= options.scoopSemitones;
      if (parts.length > 1 && span(parts[0]) < options.scoopFrames && close(parts[0], parts[1])) {
        parts[1].first = parts[0].first;
        parts.shift();
      }
      const last = parts.length - 1;
      if (last > 0 && span(parts[last]) < options.scoopFrames && close(parts[last], parts[last - 1])) {
        const end = parts.pop().frames;
        parts[last - 1].end = end[end.length - 1];
      }

      for (const part of parts) {
        const end = part.end ?? part.frames[part.frames.length - 1];
        if (end - part.first + 1 < options.minNoteFrames) continue;
        const sung = pitch(part);
        const note = Math.round(sung);
        notes.push({
          midi: note,
          cents: Math.round((sung - note) * 100),
          start: (part.first * HOP) / SAMPLE_RATE,
          duration: ((end - part.first + 1) * HOP) / SAMPLE_RATE,
        });
      }
    }
    return notes;
  }

  // ---------- Recordings ----------

  // A recording (any container the browser plays) as mono samples at SAMPLE_RATE:
  // decodeAudioData resamples to its context's rate.
  async function decode(blob) {
    const context = new OfflineAudioContext(1, 1, SAMPLE_RATE);
    const buffer = await context.decodeAudioData(await blob.arrayBuffer());
    const mono = new Float32Array(buffer.length);
    for (let channel = 0; channel < buffer.numberOfChannels; channel++) {
      const data = buffer.getChannelData(channel);
      for (let i = 0; i < data.length; i++) mono[i] += data[i] / buffer.numberOfChannels;
    }
    return mono;
  }

  function notes(samples, settings) {
    return segment(fixOctaves(frames(samples, settings).midi), settings);
  }

  const SHARP_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

  window.PitchDetector = {
    SAMPLE_RATE,
    decode,
    frames,
    notes,
    // The notes sung in a recording, in order.
    async detect(blob, settings) {
      return notes(await decode(blob), settings);
    },
    // 61 as "C#4".
    noteName(midi) {
      return SHARP_NAMES[((midi % 12) + 12) % 12] + (Math.floor(midi / 12) - 1);
    },
  };
})(window);
