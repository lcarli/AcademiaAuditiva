/**
 * Helpers for translating AA's note format ("C4", "Eb5", "F#3") to
 * VexFlow's key format ("c/4", "e/5", "f/3") plus the accidental drawn
 * next to it. Centralised here so both StaffRenderer and StaffEditor
 * agree on encoding.
 */
(function (root) {
  "use strict";

  var NOTE_PATTERN = /^([A-Ga-g])(##|bb|#|b)?(-?\d+)$/;

  /**
   * Convert an AA note name to a VexFlow key string. The accidental is
   * left out: StaffRenderer draws it as a modifier (see accidentalOf).
   * "C4"   -> "c/4"
   * "F#3"  -> "f/3"
   * "Bbb3" -> "b/3"
   * Returns null for unparseable input.
   */
  function noteToVexKey(note) {
    if (!note || typeof note !== "string") return null;
    var m = note.match(NOTE_PATTERN);
    if (!m) return null;
    return m[1].toLowerCase() + "/" + m[3];
  }

  /**
   * The accidental of an AA note name, in VexFlow's spelling:
   * "F#3" -> "#", "F##3" -> "##", "Eb5" -> "b", "Bbb3" -> "bb", "C4" -> "".
   * Returns null for unparseable input.
   */
  function accidentalOf(note) {
    if (!note || typeof note !== "string") return null;
    var m = note.match(NOTE_PATTERN);
    if (!m) return null;
    return m[2] || "";
  }

  var LETTER_SEMITONES = { c: 0, d: 2, e: 4, f: 5, g: 7, a: 9, b: 11 };
  var ACCIDENTAL_SEMITONES = { "": 0, "#": 1, "##": 2, b: -1, bb: -2 };

  /**
   * The MIDI number of an AA note name, to order notes by pitch:
   * "C4" -> 60, "B#3" -> 60, "Cb4" -> 59. Returns null for unparseable input.
   */
  function noteToMidi(note) {
    if (!note || typeof note !== "string") return null;
    var m = note.match(NOTE_PATTERN);
    if (!m) return null;
    return (parseInt(m[3], 10) + 1) * 12 + LETTER_SEMITONES[m[1].toLowerCase()] + ACCIDENTAL_SEMITONES[m[2] || ""];
  }

  /**
   * Translate a duration label to VexFlow's duration string.
   * The trailing "r" marker (used by StaffRenderer's caller) is
   * preserved by VexFlow when the user passes the full label, but
   * StaffRenderer drops it explicitly and adds a rest-flag instead.
   */
  function isRestLabel(label) {
    return typeof label === "string" && label.endsWith("r");
  }

  function stripRest(label) {
    return isRestLabel(label) ? label.slice(0, -1) : label;
  }

  function isDottedLabel(label) {
    return typeof label === "string" && stripRest(label).endsWith(".");
  }

  function baseDuration(label) {
    var l = stripRest(label);
    if (l.endsWith(".")) l = l.slice(0, -1);
    return l;
  }

  root.StaffMapping = {
    noteToVexKey: noteToVexKey,
    accidentalOf: accidentalOf,
    noteToMidi: noteToMidi,
    isRestLabel: isRestLabel,
    stripRest: stripRest,
    isDottedLabel: isDottedLabel,
    baseDuration: baseDuration,
  };
})(window);
