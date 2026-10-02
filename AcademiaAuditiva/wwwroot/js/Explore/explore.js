// Explore page: hear and see any note, interval, chord or scale; nothing is
// scored. Each choice is posted to /Explore/Play, which spells its notes and
// returns an opaque audio token (as exercise rounds do, so no sample is ever
// addressed by name). Tokens are reused for a few minutes, so replaying a
// choice skips the server.
document.addEventListener("DOMContentLoaded", () => {
  "use strict";

  const page = document.getElementById("explore");
  if (!page) return;

  const loc = document.getElementById("localizer")?.dataset ?? {};
  const playUrl = page.dataset.playUrl;
  const octaveRanges = JSON.parse(page.dataset.octaves);

  const FIRST_MIDI = 24; // C1, the lowest piano sample
  const LAST_MIDI = 107; // B7, the highest
  const MIDDLE_C = 60;
  const LETTERS = "CDEFGAB";
  const LETTER_SEMITONES = { C: 0, D: 2, E: 4, F: 5, G: 7, A: 9, B: 11 };
  const ACCIDENTALS = {
    "": { semitones: 0, sign: "" },
    "#": { semitones: 1, sign: "♯" },
    "##": { semitones: 2, sign: "♯♯" },
    b: { semitones: -1, sign: "♭" },
    bb: { semitones: -2, sign: "♭♭" },
  };
  const NOTE_PATTERN = /^([A-G])(##|bb|#|b)?(-?\d+)?$/;
  const SHARP_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
  const BLACK_KEYS = new Set([1, 3, 6, 8, 10]);
  // Each octave spans 14 grid columns: white keys take two, black keys
  // straddle the line between two white keys.
  const KEY_COLUMNS = [1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 12, 13];
  const TOKEN_REUSE_MS = 10 * 60 * 1000; // the server keeps tokens 15 minutes
  const MAX_SOUNDS = 200;
  const DEBOUNCE_MS = 250;

  const byId = (id) => document.getElementById(id);
  const tabs = [...page.querySelectorAll('[role="tab"]')];
  const panel = byId("explore-panel");
  const kindFields = [...page.querySelectorAll("[data-kinds]")];
  const rootSelect = byId("exploreRoot");
  const rootLabel = byId("exploreRootLabel");
  const octaveOutput = byId("exploreOctave");
  const octaveDown = page.querySelector('[data-octave-step="-1"]');
  const octaveUp = page.querySelector('[data-octave-step="1"]');
  const intervalSelect = byId("exploreInterval");
  const qualitySelect = byId("exploreQuality");
  const inversionSelect = byId("exploreInversion");
  const scaleSelect = byId("exploreScale");
  const toggles = [...page.querySelectorAll(".aa-explore-toggle")];
  const playButton = byId("Play");
  const result = byId("exploreResult");
  const nameEl = byId("exploreName");
  const notesEl = byId("exploreNotes");
  const errorEl = byId("exploreError");
  const keyboard = page.querySelector(".aa-explore-keyboard");
  const keysEl = byId("exploreKeys");
  const hint = byId("exploreHint");
  const staff = byId("exploreStaff");
  const reducedMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)");

  const toggleButtons = (name) =>
    [...page.querySelectorAll(`.aa-explore-toggle[data-state="${name}"] button`)];
  const pressedValue = (name) =>
    toggleButtons(name).find((button) => button.getAttribute("aria-pressed") === "true")?.value;

  // The markup's initial selections are the starting state.
  const state = {
    kind: tabs.find((tab) => tab.getAttribute("aria-selected") === "true")?.dataset.kind ?? "note",
    root: rootSelect.value,
    octave: Number(octaveOutput.textContent) || 4,
    interval: intervalSelect.value,
    direction: pressedValue("direction") ?? "ascending",
    quality: qualitySelect.value,
    inversion: Number(inversionSelect.value) || 0,
    arpeggio: pressedValue("style") === "arpeggio",
    scale: scaleSelect.value,
    scaleDirection: pressedValue("scaleDirection") ?? "ascending",
  };

  // ---------- Note names ----------
  function parseNote(text) {
    const match = NOTE_PATTERN.exec(text ?? "");
    return match && { letter: match[1], accidental: match[2] ?? "", octave: match[3] };
  }

  function toMidi(note) {
    const parsed = parseNote(note);
    if (!parsed || parsed.octave === undefined) return null;
    return 12 * (Number(parsed.octave) + 1)
      + LETTER_SEMITONES[parsed.letter]
      + ACCIDENTALS[parsed.accidental].semitones;
  }

  function pitchClass(name) {
    const parsed = parseNote(name);
    return parsed && (LETTER_SEMITONES[parsed.letter] + ACCIDENTALS[parsed.accidental].semitones + 12) % 12;
  }

  // "Db4" → "Ré♭4" in the page's language; works without the octave too.
  function displayName(note) {
    const parsed = parseNote(note);
    if (!parsed) return note;
    const letter = loc[`note${parsed.letter}`] || parsed.letter;
    return letter + ACCIDENTALS[parsed.accidental].sign + (parsed.octave ?? "");
  }

  // Black keys go by two names: "C♯4 / D♭4".
  function keyName(midi) {
    const octave = Math.floor(midi / 12) - 1;
    const sharp = SHARP_NAMES[midi % 12];
    if (sharp.length === 1) return displayName(sharp + octave);
    const flat = LETTERS[LETTERS.indexOf(sharp[0]) + 1] + "b";
    return `${displayName(sharp + octave)} / ${displayName(flat + octave)}`;
  }

  const format = (text, value) => String(text ?? "").replace("{0}", value);
  const optionText = (select, value) =>
    [...select.options].find((option) => option.value === String(value))?.textContent.trim() ?? "";
  const toggleText = (name, value) =>
    toggleButtons(name).find((button) => button.value === value)?.textContent.trim() ?? "";

  // ---------- Keyboard ----------
  const keyByMidi = new Map();
  let rovingKey = null;

  function buildKeyboard() {
    for (let midi = FIRST_MIDI; midi <= LAST_MIDI; midi++) {
      const pc = midi % 12;
      const key = document.createElement("button");
      key.type = "button";
      key.className = `aa-explore-key ${BLACK_KEYS.has(pc) ? "aa-explore-key-black" : "aa-explore-key-white"}`;
      key.dataset.midi = String(midi);
      key.tabIndex = -1;
      key.setAttribute("aria-label", keyName(midi));
      key.style.gridColumn = `${KEY_COLUMNS[pc] + 14 * Math.floor((midi - FIRST_MIDI) / 12)} / span 2`;
      if (pc === 0) {
        const label = document.createElement("span");
        label.className = "aa-explore-key-label";
        label.setAttribute("aria-hidden", "true");
        label.textContent = keyName(midi);
        key.append(label);
      }
      keyByMidi.set(midi, key);
      keysEl.append(key);
    }
  }

  // One key at a time is in the tab order; the arrow keys move it.
  function setRovingKey(key) {
    if (!key || key === rovingKey) return;
    if (rovingKey) rovingKey.tabIndex = -1;
    rovingKey = key;
    key.tabIndex = 0;
  }

  // Scrolls the keyboard only when some of the keys are out of view.
  function reveal(midis, { instant = false } = {}) {
    const keys = midis.map((midi) => keyByMidi.get(midi)).filter(Boolean);
    if (keys.length === 0 || keyboard.clientWidth === 0) return;
    const left = Math.min(...keys.map((key) => key.offsetLeft));
    const right = Math.max(...keys.map((key) => key.offsetLeft + key.offsetWidth));
    if (left >= keyboard.scrollLeft && right <= keyboard.scrollLeft + keyboard.clientWidth) return;
    keyboard.scrollTo({
      left: (left + right - keyboard.clientWidth) / 2,
      behavior: instant || reducedMotion?.matches ? "auto" : "smooth",
    });
  }

  // ---------- Result: name, notes, keys and staff ----------
  let shown = null;

  function titleFor(sound, params) {
    switch (params.kind) {
      case "interval":
        return `${optionText(intervalSelect, params.interval)} · ${toggleText("direction", params.direction)}`;
      case "chord": {
        const chord = `${displayName(sound.root)} ${optionText(qualitySelect, params.quality)}`;
        return params.inversion > 0 ? `${chord} · ${optionText(inversionSelect, params.inversion)}` : chord;
      }
      case "scale":
        return `${displayName(sound.root)} ${optionText(scaleSelect, params.scale)}`;
      default:
        return keyName(toMidi(sound.notes[0]));
    }
  }

  function noteChip(note) {
    const chip = document.createElement("li");
    chip.className = "aa-chip";
    chip.textContent = displayName(note);
    return chip;
  }

  function highlight(sound) {
    const sounding = sound.notes.map(toMidi);
    const on = new Set(sounding);
    // A lone note needs no root marker.
    const rootClass = sound.notes.length > 1 ? pitchClass(sound.root) : null;
    for (const [midi, key] of keyByMidi) {
      key.classList.toggle("is-on", on.has(midi));
      key.classList.toggle("is-root", on.has(midi) && midi % 12 === rootClass);
    }
    reveal(sounding);
  }

  function renderStaff(sound) {
    const midis = sound.notes.map(toMidi);
    const average = midis.reduce((sum, midi) => sum + midi, 0) / midis.length;
    const treble = average >= MIDDLE_C;
    // An 8va/8vb clef spares very high or low notes a tower of ledger lines.
    let clefAnnotation;
    if (treble && Math.max(...midis) > 84) clefAnnotation = "8va";
    if (!treble && Math.min(...midis) < 36) clefAnnotation = "8vb";
    const duration = sound.notes.length === 1 ? "w" : sound.notes.length === 2 ? "h" : "q";
    const notes = sound.simultaneous
      ? [{ chord: sound.notes, duration: "w" }]
      : sound.notes.map((note) => ({ note, duration }));

    staff.setAttribute("aria-label", format(loc.staffLabel, sound.notes.map(displayName).join(", ")));
    try {
      StaffRenderer.render(staff, {
        clef: treble ? "treble" : "bass",
        clefAnnotation,
        notes,
        autoStem: true,
        width: Math.min(staff.clientWidth - 24, Math.max(240, 140 + notes.length * 60)),
      });
    } catch (err) {
      staff.replaceChildren();
      console.warn("Explore: staff rendering failed", err);
    }
  }

  function show(sound, params) {
    shown = sound;
    nameEl.textContent = titleFor(sound, params);
    const several = sound.notes.length > 1;
    notesEl.replaceChildren(...(several ? sound.notes.map(noteChip) : []));
    notesEl.hidden = !several;
    highlight(sound);
    renderStaff(sound);
  }

  // ---------- Requests ----------
  class PlayError extends Error {
    constructor(status) {
      super(`Explore play failed (${status})`);
      this.status = status;
    }
  }

  function paramsFor(s) {
    const params = { kind: s.kind, root: s.root, octave: s.octave };
    switch (s.kind) {
      case "interval":
        return { ...params, interval: s.interval, direction: s.direction };
      case "chord":
        return { ...params, quality: s.quality, inversion: s.inversion, arpeggio: s.arpeggio };
      case "scale":
        return { ...params, scale: s.scale, direction: s.scaleDirection };
      default:
        return params;
    }
  }

  async function postPlay(params) {
    const response = await fetch(playUrl, {
      method: "POST",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      credentials: "same-origin",
      body: JSON.stringify(params),
    });
    if (!response.ok) throw new PlayError(response.status);
    const sound = await response.json().catch(() => null);
    if (typeof sound?.token !== "string" || !Array.isArray(sound.notes) || sound.notes.length === 0) {
      throw new PlayError(0);
    }
    return sound;
  }

  // Request JSON → { promise, expires }. Sharing the pending promise means a
  // burst of clicks on one key sends a single request.
  const sounds = new Map();

  function fetchSound(key, params) {
    const cached = sounds.get(key);
    if (cached && cached.expires > Date.now()) return cached.promise;

    const entry = { promise: postPlay(params), expires: Date.now() + TOKEN_REUSE_MS };
    sounds.delete(key);
    sounds.set(key, entry);
    entry.promise.catch(() => {
      if (sounds.get(key) === entry) sounds.delete(key);
    });
    while (sounds.size > MAX_SOUNDS) sounds.delete(sounds.keys().next().value);
    return entry.promise;
  }

  // A token can lapse before we drop it (the server restarted, say): get a
  // fresh one and try once more.
  function playSound(key, params, sound, overlap) {
    return AudioEngine.playToken(sound.token, { overlap }).catch(() => {
      sounds.delete(key);
      return fetchSound(key, params).then((fresh) => AudioEngine.playToken(fresh.token, { overlap }));
    });
  }

  // Only the latest request updates the page; older ones still play when
  // overlapping (piano keys), otherwise they are dropped.
  let latestSeq = 0;

  function request({ play = false, overlap = false } = {}) {
    const params = paramsFor(state);
    const key = JSON.stringify(params);
    const seq = ++latestSeq;
    const isLatest = () => seq === latestSeq;
    result.setAttribute("aria-busy", "true");

    fetchSound(key, params)
      .then((sound) => {
        if (isLatest()) {
          result.removeAttribute("aria-busy");
          errorEl.textContent = "";
          show(sound, params);
        }
        return play && (overlap || isLatest()) ? playSound(key, params, sound, overlap) : undefined;
      })
      .catch((err) => {
        if (!isLatest()) return;
        result.removeAttribute("aria-busy");
        errorEl.textContent = err?.status === 429 ? loc.rateLimited : loc.error;
        console.warn("Explore:", err);
      });
  }

  let pending = 0;

  // Controls wait a moment, so stepping through a menu plays only the last choice.
  function playSoon() {
    clearTimeout(pending);
    pending = setTimeout(() => request({ play: true }), DEBOUNCE_MS);
  }

  function playNow(options) {
    clearTimeout(pending);
    request({ play: true, ...options });
  }

  // ---------- Controls ----------
  function setOctave(octave) {
    const [min, max] = octaveRanges[state.kind];
    state.octave = Math.min(max, Math.max(min, octave));
    octaveOutput.textContent = String(state.octave);
    // aria-disabled (not disabled) keeps focus on a button that hits the end of the range.
    octaveDown.setAttribute("aria-disabled", String(state.octave <= min));
    octaveUp.setAttribute("aria-disabled", String(state.octave >= max));
  }

  // Triads have three positions, seventh chords four.
  function syncInversions() {
    const tones = Number(qualitySelect.selectedOptions[0]?.dataset.tones) || 3;
    for (const option of inversionSelect.options) option.disabled = Number(option.value) >= tones;
    if (state.inversion >= tones) {
      state.inversion = tones - 1;
      inversionSelect.value = String(state.inversion);
    }
  }

  for (const button of [octaveDown, octaveUp]) {
    button.addEventListener("click", () => {
      if (button.getAttribute("aria-disabled") === "true") return;
      setOctave(state.octave + Number(button.dataset.octaveStep));
      playSoon();
    });
  }

  rootSelect.addEventListener("change", () => {
    state.root = rootSelect.value;
    playSoon();
  });
  intervalSelect.addEventListener("change", () => {
    state.interval = intervalSelect.value;
    playSoon();
  });
  qualitySelect.addEventListener("change", () => {
    state.quality = qualitySelect.value;
    syncInversions();
    playSoon();
  });
  inversionSelect.addEventListener("change", () => {
    state.inversion = Number(inversionSelect.value);
    playSoon();
  });
  scaleSelect.addEventListener("change", () => {
    state.scale = scaleSelect.value;
    playSoon();
  });

  for (const group of toggles) {
    group.addEventListener("click", (event) => {
      const button = event.target.closest("button");
      if (!button || !group.contains(button)) return;
      for (const other of group.querySelectorAll("button")) {
        other.setAttribute("aria-pressed", String(other === button));
      }
      if (group.dataset.state === "style") state.arpeggio = button.value === "arpeggio";
      else state[group.dataset.state] = button.value;
      playSoon();
    });
  }

  playButton.addEventListener("click", () => playNow());

  // ---------- Tabs ----------
  function selectTab(tab) {
    for (const other of tabs) {
      const selected = other === tab;
      other.setAttribute("aria-selected", String(selected));
      other.tabIndex = selected ? 0 : -1;
    }
    panel.setAttribute("aria-labelledby", tab.id);

    const kind = tab.dataset.kind;
    state.kind = kind;
    for (const field of kindFields) field.hidden = !field.dataset.kinds.split(" ").includes(kind);
    rootLabel.textContent = loc[`root${kind[0].toUpperCase()}${kind.slice(1)}`] || rootLabel.textContent;
    hint.textContent = kind === "note" ? loc.hintNote : loc.hintRoot;
    setOctave(state.octave);
    clearTimeout(pending);
    request();
  }

  tabs.forEach((tab, index) => {
    tab.addEventListener("click", () => {
      if (tab.getAttribute("aria-selected") !== "true") selectTab(tab);
    });
    tab.addEventListener("keydown", (event) => {
      const target = {
        ArrowRight: tabs[(index + 1) % tabs.length],
        ArrowLeft: tabs[(index + tabs.length - 1) % tabs.length],
        Home: tabs[0],
        End: tabs[tabs.length - 1],
      }[event.key];
      if (!target) return;
      event.preventDefault();
      target.focus();
      if (target.getAttribute("aria-selected") !== "true") selectTab(target);
    });
  });

  // ---------- Keys ----------
  keysEl.addEventListener("click", (event) => {
    const key = event.target.closest(".aa-explore-key");
    if (!key) return;
    setRovingKey(key);
    const midi = Number(key.dataset.midi);
    state.root = SHARP_NAMES[midi % 12];
    rootSelect.value = state.root;
    setOctave(Math.floor(midi / 12) - 1);
    // On the Notes tab the keys play like a piano: notes ring on together.
    playNow({ overlap: state.kind === "note" });
  });

  keysEl.addEventListener("keydown", (event) => {
    const key = event.target.closest(".aa-explore-key");
    if (!key) return;
    const midi = Number(key.dataset.midi);
    const next = { ArrowRight: midi + 1, ArrowLeft: midi - 1, Home: FIRST_MIDI, End: LAST_MIDI }[event.key];
    if (next === undefined) return;
    event.preventDefault();
    const target = keyByMidi.get(next);
    if (!target) return;
    setRovingKey(target);
    target.focus();
  });

  // The staff is drawn to the panel's width.
  let resizeTimer = 0;
  let staffWidth = staff.clientWidth;
  window.addEventListener("resize", () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => {
      if (!shown || staff.clientWidth === staffWidth) return;
      staffWidth = staff.clientWidth;
      renderStaff(shown);
    }, 150);
  });

  buildKeyboard();
  setRovingKey(keyByMidi.get(MIDDLE_C));
  reveal([MIDDLE_C], { instant: true });
  setOctave(state.octave);
  syncInversions();
  AudioEngine.setupWaveform();
  request();
});
