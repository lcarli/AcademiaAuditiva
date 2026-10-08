// A synthetic singer for the tests of the pitch detector and of the singing
// exercises: a voice-like tone (harmonics shaped by the formants of a vowel)
// with what real singing adds to the notes: vibrato, jitter, slow drift, notes
// a little out of tune, slides between notes or into them, breath and room
// noise. Seeded, so a test hears the same singer every time.
(function (window) {
  "use strict";

  const VOWELS = {
    a: [[730, 80, 1], [1090, 90, 0.5], [2440, 120, 0.25]],
    e: [[530, 70, 1], [1840, 100, 0.45], [2480, 120, 0.3]],
    i: [[270, 60, 1], [2290, 100, 0.35], [3010, 120, 0.3]],
    o: [[570, 70, 1], [840, 80, 0.6], [2410, 120, 0.15]],
    u: [[300, 60, 1], [870, 80, 0.35], [2240, 120, 0.1]],
  };

  // Mulberry32.
  function random(seed) {
    let state = seed >>> 0;
    return () => {
      state = (state + 0x6d2b79f5) >>> 0;
      let t = state;
      t = Math.imul(t ^ (t >>> 15), t | 1);
      t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }

  function formantGain(frequency, vowel) {
    let gain = 0.04;
    for (const [center, bandwidth, weight] of VOWELS[vowel]) {
      const x = (frequency - center) / (bandwidth / 2);
      gain += weight / Math.sqrt(1 + x * x);
    }
    return gain;
  }

  function hz(midi) {
    return 440 * Math.pow(2, (midi - 69) / 12);
  }

  // notes: [{ midi, seconds }]. Returns { samples, sampleRate, sung: [{ midi, start, end }] }.
  function sing(notes, settings) {
    const o = Object.assign({
      sampleRate: 44100,
      seed: 1,
      vowel: "a",
      gain: 0.3,
      vibratoCents: 35, // half the swing
      vibratoRate: 5.5,
      detuneCents: 20, // each note up to this far off
      driftCents: 8,
      jitter: 0.004,
      legato: false, // slide from note to note, or breathe between them
      glideSeconds: 0.06,
      gapSeconds: 0.12,
      scoopCents: 60, // a detached note starts this far below and slides up
      scoopSeconds: 0.07,
      fallCents: 0, // and may fall off at the end
      fallSeconds: 0.1,
      breath: 0.02,
      room: 0.001,
      hum: 0,
      silenceSeconds: 0.4,
    }, settings);
    const rnd = random(o.seed);
    const noise = () => rnd() * 2 - 1;
    const sr = o.sampleRate;

    const plan = [];
    let time = o.silenceSeconds;
    for (const note of notes) {
      const detune = (rnd() * 2 - 1) * o.detuneCents / 100;
      plan.push({ midi: note.midi, target: note.midi + detune, start: time, end: time + note.seconds });
      time += note.seconds + (o.legato ? 0 : o.gapSeconds);
    }
    const total = Math.ceil((time + o.silenceSeconds) * sr);
    const samples = new Float32Array(total);

    let phase = 0;
    let jitter = 0;
    let drift = 0;
    const vibratoPhase = rnd() * Math.PI * 2;
    const block = 32;
    let amplitudes = [];
    let index = 0;
    for (let i = 0; i < total; i++) {
      const t = i / sr;
      while (index < plan.length && t >= plan[index].end) index++;
      const n = index < plan.length && t >= plan[index].start ? plan[index] : null;
      let loud = 0;
      let pitch = null;
      if (n) {
        const into = t - n.start;
        const left = n.end - t;
        pitch = n.target;
        const previous = plan[index - 1];
        if (o.legato && previous && into < o.glideSeconds) {
          pitch = previous.target + (n.target - previous.target) * (into / o.glideSeconds);
        } else if (!(o.legato && previous) && into < o.scoopSeconds) {
          pitch -= (o.scoopCents / 100) * (1 - into / o.scoopSeconds);
        }
        if (!(o.legato && plan[index + 1]) && left < o.fallSeconds) {
          pitch -= (o.fallCents / 100) * (1 - left / o.fallSeconds);
        }
        // Vibrato comes in after the onset.
        const depth = Math.min(1, Math.max(0, (into - 0.2) / 0.2));
        pitch += depth * (o.vibratoCents / 100) * Math.sin(2 * Math.PI * o.vibratoRate * t + vibratoPhase);
        if (i % block === 0) {
          drift += noise() * 0.02;
          drift *= 0.995;
          jitter = jitter * 0.9 + noise() * 0.1;
        }
        pitch += (drift * o.driftCents * 3) / 100;
        const attack = o.legato && previous ? 1 : Math.min(1, into / 0.04);
        const release = o.legato && plan[index + 1] ? 1 : Math.min(1, left / 0.06);
        loud = attack * release;
      }

      let value = 0;
      if (pitch !== null && loud > 0) {
        const f0 = hz(pitch) * (1 + o.jitter * jitter);
        if (i % block === 0 || amplitudes.length === 0) {
          amplitudes = [];
          for (let k = 1; k * f0 < Math.min(5000, sr / 2); k++) {
            amplitudes.push(Math.pow(k, -1.2) * formantGain(k * f0, o.vowel));
          }
        }
        phase += (2 * Math.PI * f0) / sr;
        if (phase > 2 * Math.PI * 1000) phase -= 2 * Math.PI * 1000;
        for (let k = 0; k < amplitudes.length; k++) value += amplitudes[k] * Math.sin((k + 1) * phase);
        value = value * loud * 0.5 + noise() * o.breath * loud;
      }
      // Mains hum has harmonics, which a pitch tracker could take for a low voice.
      value += noise() * o.room + o.hum * (Math.sin(2 * Math.PI * 60 * t) + 0.5 * Math.sin(2 * Math.PI * 120 * t) + 0.3 * Math.sin(2 * Math.PI * 180 * t));
      samples[i] = value * o.gain;
    }
    return { samples, sampleRate: sr, sung: plan.map((p) => ({ midi: p.midi, start: p.start, end: p.end })) };
  }

  // 16-bit PCM WAV, as a recording to decode.
  function wav(samples, sampleRate) {
    const buffer = new ArrayBuffer(44 + samples.length * 2);
    const view = new DataView(buffer);
    const text = (offset, s) => [...s].forEach((c, i) => view.setUint8(offset + i, c.charCodeAt(0)));
    text(0, "RIFF");
    view.setUint32(4, 36 + samples.length * 2, true);
    text(8, "WAVE");
    text(12, "fmt ");
    view.setUint32(16, 16, true);
    view.setUint16(20, 1, true);
    view.setUint16(22, 1, true);
    view.setUint32(24, sampleRate, true);
    view.setUint32(28, sampleRate * 2, true);
    view.setUint16(32, 2, true);
    view.setUint16(34, 16, true);
    text(36, "data");
    view.setUint32(40, samples.length * 2, true);
    for (let i = 0; i < samples.length; i++) {
      const s = Math.max(-1, Math.min(1, samples[i]));
      view.setInt16(44 + i * 2, s < 0 ? s * 0x8000 : s * 0x7fff, true);
    }
    return new Blob([buffer], { type: "audio/wav" });
  }

  window.SyntheticVoice = { sing, wav, random, vowels: Object.keys(VOWELS) };
})(window);
